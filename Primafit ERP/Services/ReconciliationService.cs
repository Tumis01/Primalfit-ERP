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

        private static bool LooksLikeHeader(string line) => line.ToLower().Contains("date") && line.ToLower().Contains("amount");

        // 1. Start Reconciliation (Import CSV - FIXED)
        public async Task<(string error, Guid? reconId)> StartReconciliationAsync(
            Guid companyId, Guid bankAccountId, DateOnly statementDate, decimal endingBalance, Stream csvStream)
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
                Status = ReconStatus.Draft
            };

            // FIX: Using StreamReader with Async Loop
            using var reader = new StreamReader(csvStream);
            string? line;
            bool firstLine = true;

            // This loop replaces !reader.EndOfStream to fix the exception
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (firstLine)
                {
                    firstLine = false;
                    if (LooksLikeHeader(line)) continue;
                }

                var cols = line.Split(',');
                if (cols.Length < 3) continue;

                // Format: Date, Description, Amount, Reference(opt)
                if (DateOnly.TryParse(cols[0].Trim(), out var d) && decimal.TryParse(cols[2].Trim(), out var a))
                {
                    recon.StatementLines.Add(new BankStatementLine
                    {
                        CompanyId = companyId,
                        Date = d,
                        Description = cols[1].Trim(),
                        Amount = a,
                        Reference = cols.Length > 3 ? cols[3].Trim() : null,
                        IsMatched = false
                    });
                }
            }

            if (recon.StatementLines.Count == 0) return ("No valid lines found in CSV.", null);

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

            var txns = await ctx.GLTransactions
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId &&
                            t.AccountId == recon.BankAccountId &&
                            !t.IsReconciled &&
                            t.PostingDate <= recon.StatementDate)
                .OrderByDescending(t => t.PostingDate)
                .ToListAsync();

            return new ReconViewModel
            {
                Reconciliation = recon,
                CandidateGLTransactions = txns
            };
        }

        // 3. Auto Match
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
                    (t.Debit - t.Credit) == line.Amount &&
                    Math.Abs(t.PostingDate.DayNumber - line.Date.DayNumber) <= toleranceDays);

                if (match != null)
                {
                    line.IsMatched = true;
                    line.MatchedGLTransactionId = match.Id;
                    line.MatchedByUserId = "ANONYMOUS_USER";
                    line.MatchedAt = DateTime.UtcNow;
                    txns.Remove(match);
                    count++;
                }
            }
            await ctx.SaveChangesAsync();
            return count;
        }

        // 4. Manual Match
        public async Task<string> MatchManuallyAsync(Guid companyId, Guid bankLineId, Guid glTxnId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var line = await ctx.BankStatementLines.FirstOrDefaultAsync(l => l.Id == bankLineId && l.CompanyId == companyId);
            if (line == null) return "Bank line not found.";

            var txn = await ctx.GLTransactions.FirstOrDefaultAsync(t => t.Id == glTxnId && t.CompanyId == companyId);
            if (txn == null) return "GL Transaction not found.";

            if (line.IsMatched || txn.IsReconciled) return "Already matched/reconciled.";

            line.IsMatched = true;
            line.MatchedGLTransactionId = txn.Id;
            line.MatchedByUserId = "ANONYMOUS_USER";
            line.MatchedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 5. Create Adjustment
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

        // 6. Finalize
        public async Task<string> FinalizeReconciliationAsync(Guid companyId, Guid reconId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var recon = await ctx.BankReconciliations.Include(r => r.StatementLines).FirstOrDefaultAsync(r => r.Id == reconId);
            if (recon == null) return "Recon not found.";

            var matchedTxnIds = recon.StatementLines
                .Where(l => l.MatchedGLTransactionId != null)
                .Select(l => l.MatchedGLTransactionId!.Value)
                .ToList();

            var txns = await ctx.GLTransactions.Where(t => matchedTxnIds.Contains(t.Id)).ToListAsync();

            foreach (var t in txns)
            {
                t.IsReconciled = true;
                t.ReconciledByUserId = "ANONYMOUS_USER";
                t.ReconciledAt = DateTime.UtcNow;
                t.BankReconciliationId = recon.Id;
            }

            recon.Status = ReconStatus.Approved;
            recon.ApprovedByUserId = "ANONYMOUS_USER";
            recon.ApprovedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync();
            return "Success";
        }
    }
}