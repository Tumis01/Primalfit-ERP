using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System.Globalization;

namespace Primafit_ERP.Services
{
    public class ReconciliationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly StatementImportService _statementImport;

        public ReconciliationService(
        IDbContextFactory<AppDbContext> dbFactory,
        StatementImportService statementImport)
        {
            _dbFactory = dbFactory;
            _statementImport = statementImport;
        }

        public async Task<(string err, Guid id)> StartReconciliationAsync(
             Guid companyId,
             Guid bankSegCoaId, // CHANGED: Renamed for clarity
             DateOnly statementDate,
             decimal endingBalance,
             ReconType type,
             IBrowserFile? uploadedFile,
             string userId)
        {
            try
            {
                if (companyId == Guid.Empty) return ("Select a company.", Guid.Empty);
                if (bankSegCoaId == Guid.Empty) return ("Select a bank account.", Guid.Empty);

                await using var db = await _dbFactory.CreateDbContextAsync();

                // 1. SEARCH FOR EXISTING DRAFT
                var existingDraft = await db.BankReconciliations
                    .Include(r => r.StatementLines)
                    .FirstOrDefaultAsync(r =>
                        r.CompanyId == companyId &&
                        r.BankAccountId == bankSegCoaId && // CHANGED
                        r.StatementDate == statementDate &&
                        r.Status == ReconStatus.Draft);

                if (existingDraft != null)
                {
                    existingDraft.StatementEndingBalance = endingBalance;
                    existingDraft.Type = type;
                    existingDraft.PreparedByUserId = userId;

                    if (type == ReconType.Automatic)
                    {
                        if (uploadedFile == null && !existingDraft.StatementLines.Any())
                            return ("Please upload a bank statement file.", Guid.Empty);

                        if (uploadedFile != null)
                        {
                            // Parse & Replace logic (Same as your code)
                            await using var stream = uploadedFile.OpenReadStream(10 * 1024 * 1024);
                            var rows = await _statementImport.ParseAsync(uploadedFile.Name, uploadedFile.ContentType, stream);

                            if (rows.Count == 0) return ("No valid transactions found.", Guid.Empty);

                            await using var tx = await db.Database.BeginTransactionAsync();
                            var oldLines = await db.BankStatementLines.Where(x => x.ReconciliationId == existingDraft.Id).ToListAsync();
                            if (oldLines.Count > 0) db.BankStatementLines.RemoveRange(oldLines);

                            foreach (var r in rows)
                            {
                                db.BankStatementLines.Add(new BankStatementLine
                                {
                                    ReconciliationId = existingDraft.Id,
                                    Date = r.Date,
                                    Reference = r.Reference,
                                    Description = r.Description ?? "Imported",
                                    Amount = r.Amount,
                                    IsMatched = false
                                });
                            }
                            await db.SaveChangesAsync();
                            await tx.CommitAsync();
                        }
                    }

                    await db.SaveChangesAsync();
                    return ("", existingDraft.Id);
                }

                // --- NEW RECONCILIATION ---
                var recon = new BankReconciliation
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    BankAccountId = bankSegCoaId, // CHANGED
                    StatementDate = statementDate,
                    StatementEndingBalance = endingBalance,
                    Type = type,
                    Status = ReconStatus.Draft,
                    PreparedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                if (type == ReconType.Automatic)
                {
                    if (uploadedFile == null) return ("Please upload a file.", Guid.Empty);
                    await using var stream = uploadedFile.OpenReadStream(10 * 1024 * 1024);
                    var rows = await _statementImport.ParseAsync(uploadedFile.Name, uploadedFile.ContentType, stream);
                    if (rows.Count == 0) return ("File is empty.", Guid.Empty);

                    foreach (var r in rows)
                    {
                        recon.StatementLines.Add(new BankStatementLine
                        {
                            ReconciliationId = recon.Id,
                            Date = r.Date,
                            Reference = r.Reference,
                            Description = r.Description ?? "Imported",
                            Amount = r.Amount,
                            IsMatched = false
                        });
                    }
                }

                db.BankReconciliations.Add(recon);
                await db.SaveChangesAsync();
                return ("", recon.Id);
            }
            catch (Exception ex)
            {
                return ($"Error: {ex.Message}", Guid.Empty);
            }
        }

        public async Task<ReconViewModel?> GetReconDataAsync(Guid companyId, Guid reconId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var recon = await db.BankReconciliations
                .FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);

            if (recon == null) return null;

            // Load Lines
            recon.StatementLines = await db.BankStatementLines
                .Where(l => l.ReconciliationId == reconId)
                .OrderBy(l => l.Date)
                .ToListAsync();

            // Load Candidates (GL Transactions)
            // CRITICAL FIX: Query SegCoaId, not AccountId
            var txns = await db.GLTransactions
                .Where(t =>
                    t.CompanyId == companyId &&
                    t.SegCoaId == recon.BankAccountId &&
                    (t.BankReconciliationId == null || t.BankReconciliationId == reconId) &&
                    t.PostingDate <= recon.StatementDate)
                .OrderBy(t => t.PostingDate)
                .ToListAsync();

            return new ReconViewModel
            {
                Reconciliation = recon,
                CandidateGLTransactions = txns
            };
        }

        public async Task ToggleTransactionClearedAsync(Guid txnId, Guid reconId, bool isChecked)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var txn = await db.GLTransactions.FirstOrDefaultAsync(t => t.Id == txnId);
            if (txn == null) return;

            if (isChecked)
                txn.BankReconciliationId = reconId;
            else if (txn.BankReconciliationId == reconId)
                txn.BankReconciliationId = null;

            await db.SaveChangesAsync();
        }

        public async Task<int> AutoMatchAsync(Guid companyId, Guid reconId, decimal tolerance)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var recon = await db.BankReconciliations.FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);
            if (recon == null) return 0;

            var statementLines = await db.BankStatementLines.Where(l => l.ReconciliationId == reconId && !l.IsMatched).ToListAsync();

            // CRITICAL FIX: Query SegCoaId
            var glTransactions = await db.GLTransactions
                .Where(t =>
                    t.CompanyId == companyId &&
                    t.SegCoaId == recon.BankAccountId && // CHANGED
                    t.BankReconciliationId == null &&
                    t.PostingDate <= recon.StatementDate.AddDays(10)) // Grace period
                .ToListAsync();

            int matched = 0;

            foreach (var line in statementLines)
            {
                var stAmt = line.Amount;

                // Simple Auto-Match logic
                var match = glTransactions.FirstOrDefault(t =>
                {
                    // GL Amount: Debit - Credit. 
                    // If Bank Statement +100 (Deposit), GL should be Debit 100 (Asset Increase).
                    // If Bank Statement -100 (Payment), GL should be Credit 100 (Asset Decrease).

                    var glNet = t.Debit - t.Credit;

                    var amountMatches = Math.Abs(glNet - stAmt) <= tolerance;
                    var dateMatches = Math.Abs(t.PostingDate.DayNumber - line.Date.DayNumber) <= 5;

                    return amountMatches && dateMatches;
                });

                if (match == null) continue;

                match.BankReconciliationId = reconId;
                line.IsMatched = true;
                line.MatchedGLTransactionId = match.Id;

                glTransactions.Remove(match); // Prevent double use
                matched++;
            }

            await db.SaveChangesAsync();
            return matched;
        }

        // MatchManuallyAsync and FinalizeReconciliationAsync remain largely the same, 
        // just ensure any queries use SegCoaId if they touch GLTransactions directly.
        public async Task<string> MatchManuallyAsync(Guid companyId, Guid statementLineId, Guid txnId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var line = await db.BankStatementLines.FirstOrDefaultAsync(l => l.Id == statementLineId);
                if (line == null) return "Statement line not found.";

                var txn = await db.GLTransactions.FirstOrDefaultAsync(t => t.Id == txnId && t.CompanyId == companyId);
                if (txn == null) return "GL transaction not found.";

                if (line.IsMatched || txn.BankReconciliationId != null) return "Item already matched.";

                txn.BankReconciliationId = line.ReconciliationId;
                line.IsMatched = true;
                line.MatchedGLTransactionId = txn.Id;

                await db.SaveChangesAsync();
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public async Task<string> FinalizeReconciliationAsync(Guid companyId, Guid reconId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var recon = await db.BankReconciliations.FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);
            if (recon == null) return "Reconciliation not found.";

            recon.Status = ReconStatus.Finalized;
            recon.ApprovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return "Success";
        }
    }
}