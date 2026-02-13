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

        // VIEW EXPECTS: (string err, Guid id)
        // VIEW EXPECTS: (string err, Guid id)
        // VIEW EXPECTS: (string err, Guid id)
        public async Task<(string err, Guid id)> StartReconciliationAsync(
             Guid companyId,
             Guid bankAccountId,
             DateOnly statementDate,
             decimal endingBalance,
             ReconType type,
             IBrowserFile? uploadedFile,
             string userId)
        {
            try
            {
                if (companyId == Guid.Empty) return ("Select a company.", Guid.Empty);
                if (bankAccountId == Guid.Empty) return ("Select a bank account.", Guid.Empty);

                await using var db = await _dbFactory.CreateDbContextAsync();

                // 1. SEARCH FOR EXISTING DRAFT
                var existingDraft = await db.BankReconciliations
                    .Include(r => r.StatementLines)
                    .FirstOrDefaultAsync(r =>
                        r.CompanyId == companyId &&
                        r.BankAccountId == bankAccountId &&
                        r.StatementDate == statementDate &&
                        r.Status == ReconStatus.Draft);

                if (existingDraft != null)
                {
                    existingDraft.StatementEndingBalance = endingBalance;
                    existingDraft.Type = type;
                    existingDraft.PreparedByUserId = userId;

                    if (type == ReconType.Automatic)
                    {
                        if (uploadedFile == null)
                            return ("Please upload a bank statement file for automatic reconciliation.", Guid.Empty);

                        // ✅ Parse FIRST (no DB changes yet)
                        await using var stream = uploadedFile.OpenReadStream(10 * 1024 * 1024);
                        var rows = await _statementImport.ParseAsync(uploadedFile.Name, uploadedFile.ContentType, stream);

                        if (rows.Count == 0)
                            return ("No valid transactions found in the file.", Guid.Empty);

                        // ✅ Replace lines atomically
                        await using var tx = await db.Database.BeginTransactionAsync();

                        // remove old lines
                        var oldLines = await db.BankStatementLines
                            .Where(x => x.ReconciliationId == existingDraft.Id)
                            .ToListAsync();

                        if (oldLines.Count > 0)
                            db.BankStatementLines.RemoveRange(oldLines);

                        // add new lines
                        foreach (var r in rows)
                        {
                            db.BankStatementLines.Add(new BankStatementLine
                            {
                                Id = Guid.NewGuid(),
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

                    await db.SaveChangesAsync();
                    return ("", existingDraft.Id);
                }


                // --- NEW RECONCILIATION ---
                var recon = new BankReconciliation
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    BankAccountId = bankAccountId,
                    StatementDate = statementDate,
                    StatementEndingBalance = endingBalance,
                    Type = type,
                    Status = ReconStatus.Draft,
                    PreparedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                if (type == ReconType.Automatic)
                {
                    if (uploadedFile == null) return ("Please upload a bank statement file for automatic reconciliation.", Guid.Empty);

                    await using var stream = uploadedFile.OpenReadStream(10 * 1024 * 1024);
                    var rows = await _statementImport.ParseAsync(uploadedFile.Name, uploadedFile.ContentType, stream);

                    if (rows.Count == 0) return ("File is empty or format is incorrect.", Guid.Empty);

                    foreach (var r in rows)
                    {
                        recon.StatementLines.Add(new BankStatementLine
                        {
                            Id = Guid.NewGuid(),
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

        // VIEW EXPECTS: ReconViewModel?
        public async Task<ReconViewModel?> GetReconDataAsync(Guid companyId, Guid reconId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var recon = await db.BankReconciliations
                .FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);

            if (recon == null) return null;

            var lines = await db.BankStatementLines
                .Where(l => l.ReconciliationId == reconId)
                .OrderBy(l => l.Date)
                .ToListAsync();

            recon.StatementLines = lines;

            var txns = await db.GLTransactions
                .Where(t =>
                    t.CompanyId == companyId &&
                    t.AccountId == recon.BankAccountId &&
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


        // VIEW EXPECTS: ToggleCleared(txnId, reconId, isChecked)
        public async Task ToggleTransactionClearedAsync(Guid txnId, Guid reconId, bool isChecked)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var txn = await db.GLTransactions.FirstOrDefaultAsync(t => t.Id == txnId);
            if (txn == null) return;

            if (isChecked)
            {
                txn.BankReconciliationId = reconId;
            }
            else
            {
                // Only uncheck if it was checked against THIS recon
                if (txn.BankReconciliationId == reconId)
                    txn.BankReconciliationId = null;
            }

            await db.SaveChangesAsync();
        }

        // VIEW EXPECTS: AutoMatchAsync(companyId, reconId, tolerance)
        public async Task<int> AutoMatchAsync(Guid companyId, Guid reconId, decimal tolerance)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var recon = await db.BankReconciliations
                .FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);

            if (recon == null) return 0;

            recon.StatementLines = await db.BankStatementLines
                .Where(l => l.ReconciliationId == reconId)
                .ToListAsync();


            if (recon == null) return 0;

            var graceDays = 10;
            var dateWindowDays = 10;

            var gl = await db.GLTransactions
                .Where(t =>
                    t.CompanyId == companyId &&
                    t.AccountId == recon.BankAccountId &&
                    t.BankReconciliationId == null &&
                    t.PostingDate <= recon.StatementDate.AddDays(graceDays))
                .OrderBy(t => t.PostingDate)
                .ToListAsync();

            int matched = 0;

            foreach (var line in recon.StatementLines.Where(l => !l.IsMatched))
            {
                var stAmt = line.Amount;

                var match = gl.FirstOrDefault(t =>
                {
                    var glAbs = Math.Abs(t.Debit - t.Credit);
                    var stAbs = Math.Abs(line.Amount);

                    var amountMatches = Math.Abs(glAbs - stAbs) <= tolerance;

                    var dateMatches =
                        Math.Abs(t.PostingDate.DayNumber - line.Date.DayNumber) <= dateWindowDays;

                    return amountMatches && dateMatches;
                });


                if (match == null) continue;

                match.BankReconciliationId = reconId;
                line.IsMatched = true;
                line.MatchedGLTransactionId = match.Id;

                gl.Remove(match); // Prevent double matching
                matched++;
            }

            await db.SaveChangesAsync();
            return matched;
        }

        // VIEW EXPECTS: string err
        public async Task<string> MatchManuallyAsync(Guid companyId, Guid statementLineId, Guid txnId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();

                var line = await db.BankStatementLines.FirstOrDefaultAsync(l => l.Id == statementLineId);
                if (line == null) return "Statement line not found.";

                var recon = await db.BankReconciliations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == line.ReconciliationId && r.CompanyId == companyId);

                if (recon == null) return "Reconciliation context missing.";

                var txn = await db.GLTransactions.FirstOrDefaultAsync(t => t.Id == txnId && t.CompanyId == companyId);
                if (txn == null) return "GL transaction not found.";

                if (line.IsMatched) return "Statement line already matched.";
                if (txn.BankReconciliationId != null) return "GL transaction already reconciled.";

                txn.BankReconciliationId = line.ReconciliationId;
                line.IsMatched = true;
                line.MatchedGLTransactionId = txn.Id;

                await db.SaveChangesAsync();
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<string> FinalizeReconciliationAsync(Guid companyId, Guid reconId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var recon = await db.BankReconciliations.FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);
                if (recon == null) return "Reconciliation not found.";

                recon.Status = ReconStatus.Finalized;
                recon.ApprovedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();
                return "Success";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}