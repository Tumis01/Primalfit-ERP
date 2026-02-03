using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class ReconciliationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _gl;

        public ReconciliationService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService gl)
        {
            _dbFactory = dbFactory;
            _gl = gl;
        }

        // 1. Start Reconciliation
        public async Task<(string error, Guid? reconId)> StartReconciliationAsync(
            Guid companyId, Guid bankAccountId, DateOnly statementDate, decimal endingBalance,
            ReconType type, Stream? csvStream = null)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var period = await ctx.AccountingPeriods.AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.StartDate <= statementDate && p.EndDate >= statementDate);

            if (period == null) return ("No accounting period found for this statement date.", null);
            if (period.IsClosed) return ("The accounting period is closed.", null);

            var recon = new BankReconciliation
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BankAccountId = bankAccountId,
                StatementDate = statementDate,
                StatementEndingBalance = endingBalance,
                PreparedByUserId = "ANONYMOUS_USER",
                Status = ReconStatus.Draft,
                Type = type
            };

            // Only process CSV if Automatic
            if (type == ReconType.Automatic && csvStream != null)
            {
                using var reader = new StreamReader(csvStream);
                await reader.ReadLineAsync(); // Skip Header

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cols = line.Split(',');
                    if (cols.Length < 3) continue;

                    // Template: Date, Reference, Amount
                    if (DateOnly.TryParse(cols[0].Trim(), out var d) && decimal.TryParse(cols[2].Trim(), out var amount))
                    {
                        recon.StatementLines.Add(new BankStatementLine
                        {
                            CompanyId = companyId,
                            Date = d,
                            Reference = cols[1].Trim(),
                            Description = amount > 0 ? "Bank Deposit" : "Bank Withdrawal",
                            Amount = Math.Abs(amount),
                            IsMatched = false
                        });
                    }
                }
                if (recon.StatementLines.Count == 0) return ("No valid lines found in CSV.", null);
            }

            ctx.BankReconciliations.Add(recon);
            await ctx.SaveChangesAsync();
            return (string.Empty, recon.Id);
        }

        // 2. Get Data
        public async Task<ReconViewModel> GetReconDataAsync(Guid companyId, Guid reconId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var recon = await ctx.BankReconciliations
                .Include(r => r.StatementLines)
                .FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);

            if (recon == null) throw new InvalidOperationException("Reconciliation not found.");

            // Fetch Transactions: Include unreconciled OR those matched to this specific recon
            var txns = await ctx.GLTransactions
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId &&
                            t.AccountId == recon.BankAccountId &&
                            (!t.IsReconciled || t.BankReconciliationId == reconId) &&
                            t.PostingDate <= recon.StatementDate)
                .OrderBy(t => t.PostingDate)
                .ToListAsync();

            return new ReconViewModel
            {
                Reconciliation = recon,
                CandidateGLTransactions = txns
            };
        }

        // 3. Manual Mode: Toggle Cleared Status
        public async Task ToggleTransactionClearedAsync(Guid txnId, Guid reconId, bool isCleared)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var txn = await ctx.GLTransactions.FindAsync(txnId);
            if (txn != null)
            {
                // Link/Unlink this specific recon ID
                txn.BankReconciliationId = isCleared ? reconId : null;
                await ctx.SaveChangesAsync();
            }
        }

        // 4. Auto Match
        public async Task<int> AutoMatchAsync(Guid companyId, Guid reconId, int toleranceDays)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var recon = await ctx.BankReconciliations.Include(r => r.StatementLines).FirstOrDefaultAsync(r => r.Id == reconId);
            if (recon == null) return 0;

            var txns = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId &&
                            t.AccountId == recon.BankAccountId &&
                            !t.IsReconciled &&
                            t.PostingDate <= recon.StatementDate)
                .ToListAsync();

            int count = 0;

            foreach (var line in recon.StatementLines.Where(l => !l.IsMatched))
            {
                var match = txns.FirstOrDefault(t =>
                    Math.Abs(t.Debit - t.Credit) == line.Amount &&
                    Math.Abs(t.PostingDate.DayNumber - line.Date.DayNumber) <= toleranceDays);

                if (match != null)
                {
                    line.IsMatched = true;
                    line.MatchedGLTransactionId = match.Id;
                    match.BankReconciliationId = recon.Id; // Link it

                    count++;
                    txns.Remove(match);
                }
            }
            await ctx.SaveChangesAsync();
            return count;
        }

        // 5. Manual Match (For Auto Mode Correction) - RE-ADDED
        public async Task<string> MatchManuallyAsync(Guid companyId, Guid bankLineId, Guid glTxnId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var line = await ctx.BankStatementLines.FirstOrDefaultAsync(l => l.Id == bankLineId && l.CompanyId == companyId);
            if (line == null) return "Bank line not found.";
            if (line.IsMatched) return "Bank line is already matched.";

            var txn = await ctx.GLTransactions.FirstOrDefaultAsync(t => t.Id == glTxnId && t.CompanyId == companyId);
            if (txn == null) return "GL Transaction not found.";
            if (txn.IsReconciled) return "Transaction is already reconciled.";

            // Perform the match
            line.IsMatched = true;
            line.MatchedGLTransactionId = txn.Id;
            txn.BankReconciliationId = line.ReconciliationId; // Link GL to this Recon

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 6. Finalize
        public async Task<string> FinalizeReconciliationAsync(Guid companyId, Guid reconId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var recon = await ctx.BankReconciliations.Include(r => r.StatementLines).FirstOrDefaultAsync(r => r.Id == reconId);
            if (recon == null) return "Recon not found.";

            // Lock all GL transactions linked to this recon
            var linkedTxns = await ctx.GLTransactions
                .Where(t => t.BankReconciliationId == reconId && t.CompanyId == companyId)
                .ToListAsync();

            foreach (var t in linkedTxns)
            {
                t.IsReconciled = true; // Permanent Lock
                t.ReconciledByUserId = "ANONYMOUS_USER";
                t.ReconciledAt = DateTime.UtcNow;
            }

            recon.Status = ReconStatus.Approved;
            recon.ApprovedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync();
            return "Success";
        }

        // 7. Create Adjustment (Stub)
        public async Task<string> CreateAdjustmentAsync(Guid companyId, Guid reconId, string description, decimal amount, Guid expenseAccountId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var recon = await ctx.BankReconciliations.FindAsync(reconId);
            if (recon == null) return "Recon not found.";

            var lines = new List<GLJournalLine>
            {
                new GLJournalLine { AccountId = expenseAccountId, Debit = amount, Credit = 0, Reference = description },
                new GLJournalLine { AccountId = recon.BankAccountId, Debit = 0, Credit = amount, Reference = "Bank Adjustment" }
            };

            var (err, _) = await _gl.CreateDraftBatchAsync(
                companyId,
                recon.StatementDate,
                $"RECON-ADJ {DateTime.Now:HHmm}",
                description,
                "RECON-ADJ",
                description,
                lines,
                BatchType.ReconciliationAdjustment);

            return err;
        }
    }
}