using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class GLOperationsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public GLOperationsService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // =========================================================
        // Helpers
        // =========================================================
        private static bool IsBalanced(IEnumerable<GLJournalLine> lines)
            => lines.Sum(x => x.Debit) == lines.Sum(x => x.Credit);

        private async Task<AccountingPeriod> ResolvePeriodOrThrow(AppDbContext ctx, Guid companyId, DateOnly txnDate)
        {
            var periods = await ctx.AccountingPeriods
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && !p.IsClosed)
                .ToListAsync();

            var period = periods.FirstOrDefault(p => p.StartDate <= txnDate && p.EndDate >= txnDate);

            if (period == null)
            {
                var availableRanges = string.Join(", ",
                    periods.Select(p => $"{p.StartDate:yyyy-MM-dd} to {p.EndDate:yyyy-MM-dd}"));

                throw new InvalidOperationException(
                    $"No active accounting period found for {txnDate:yyyy-MM-dd}. Open periods are: [{availableRanges}]");
            }

            return period;
        }

        /// <summary>
        /// Ensures all selected Seg COA IDs exist for the company.
        /// If requireAllowJournal == true, ensures AllowJournal == true for all.
        /// </summary>
        private async Task<string?> ValidateSegmentedAccountsAsync(
            AppDbContext ctx,
            Guid companyId,
            List<GLJournalLine> lines,
            bool requireAllowJournal)
        {
            var ids = lines.Select(x => x.SegCoaId).Where(x => x != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) return "No valid accounts selected.";

            var accounts = await ctx.SegChartOfAccounts
                .AsNoTracking()
                .Where(a => a.CompanyId == companyId && ids.Contains(a.Id))
                .Select(a => new { a.Id, a.AllowJournal })
                .ToListAsync();

            if (accounts.Count != ids.Count)
                return "One or more selected accounts do not exist in the Segmented COA for this company.";

            if (requireAllowJournal)
            {
                var blocked = accounts.Where(a => !a.AllowJournal).Select(a => a.Id).ToList();
                if (blocked.Any())
                    return "One or more selected accounts are not allowed for Journal posting (AllowJournal = No).";
            }

            return null;
        }

        // =========================================================
        // 1) Create Standard Journal Entry (Wrapper)
        // =========================================================
        public async Task<(string error, Guid? batchId)> CreateJournalEntryAsync(
            Guid companyId,
            DateOnly txnDate,
            string batchName,
            string? description,
            List<GLJournalLine> lines)
        {
            string journalNumber = $"JV-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

            return await CreateDraftBatchAsync(
                companyId,
                txnDate,
                batchName,
                description,
                journalNumber,
                description,              // Narration
                lines,
                BatchType.Standard);
        }

        // =========================================================
        // 2) CORE: Create Draft Batch (The Engine)
        // =========================================================
        public async Task<(string error, Guid? batchId)> CreateDraftBatchAsync(
            Guid companyId,
            DateOnly txnDate,
            string batchName,
            string? description,
            string journalNumber,
            string? narration,
            List<GLJournalLine> lines,
            BatchType type)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            // Clean lines
            var cleanLines = lines
                .Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0))
                .ToList();

            if (cleanLines.Count == 0) return ("No valid lines.", null);

            // Standard journals must be balanced immediately
            if (type == BatchType.Standard && !IsBalanced(cleanLines))
                return ("Journal is not balanced (Debits must equal Credits).", null);

            // Validate accounting period
            AccountingPeriod period;
            try
            {
                period = await ResolvePeriodOrThrow(ctx, companyId, txnDate);
            }
            catch (Exception ex)
            {
                return (ex.Message, null);
            }

            // Validate Seg COA selection
            // - Standard/Migration journals should only allow AllowJournal accounts
            var requireAllowJournal = type == BatchType.Standard || type == BatchType.Migration;
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, cleanLines, requireAllowJournal);
            if (!string.IsNullOrWhiteSpace(acctErr)) return (acctErr!, null);

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = description,
                Type = type,
                Status = BatchStatus.Draft,
                CreatedByUserId = "ANONYMOUS_USER"
            };

            batch.Journals.Add(new GLJournalHeader
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                JournalNumber = journalNumber,
                Narration = narration,
                TransactionDate = txnDate,
                Lines = cleanLines
            });

            ctx.GLBatches.Add(batch);
            await ctx.SaveChangesAsync();

            return (string.Empty, batch.Id);
        }

        // =========================================================
        // 3) Opening Balances (Migration)
        // =========================================================
        public async Task<string> CreateOpeningBalanceMigrationAsync(
            Guid companyId,
            DateOnly migrationDate,
            string batchName,
            List<GLJournalLine> inputLines)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var cleanLines = inputLines
                .Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0))
                .ToList();

            if (cleanLines.Count == 0) return "Enter at least one valid line.";

            AccountingPeriod period;
            try
            {
                period = await ResolvePeriodOrThrow(ctx, companyId, migrationDate);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            // Only AllowJournal accounts for migration too (keeps it consistent)
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, cleanLines, requireAllowJournal: true);
            if (!string.IsNullOrWhiteSpace(acctErr)) return acctErr!;

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = "System Migration",
                Type = BatchType.Migration,
                Status = BatchStatus.Draft,
                CreatedByUserId = "ANONYMOUS_USER"
            };

            batch.Journals.Add(new GLJournalHeader
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                JournalNumber = "OPENING-BAL",
                TransactionDate = migrationDate,
                Lines = cleanLines
            });

            ctx.GLBatches.Add(batch);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // =========================================================
        // 4) Release
        // =========================================================
        public async Task<string> ReleaseBatchAsync(Guid companyId, Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.GLBatches
                .Include(b => b.Journals)
                .ThenInclude(j => j.Lines)
                .FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be released.";

            foreach (var j in batch.Journals)
                if (!IsBalanced(j.Lines)) return $"Journal '{j.JournalNumber}' is not balanced.";

            batch.Status = BatchStatus.Ready;
            batch.ReleasedByUserId = "ANONYMOUS_USER";
            batch.ReleasedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // =========================================================
        // 5) Reject
        // =========================================================
        public async Task<string> RejectBatchAsync(Guid companyId, Guid batchId, string reason)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.GLBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);
            if (batch == null) return "Batch not found.";

            batch.Status = BatchStatus.Rejected;
            batch.RejectedByUserId = "ANONYMOUS_USER";
            batch.RejectedAt = DateTime.UtcNow;
            batch.RejectionReason = reason;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // =========================================================
        // Reports (Segmented COA based)
        // =========================================================
        public async Task<List<TrialBalanceRow>> GetTrialBalanceAsync(Guid companyId, Guid periodId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var query =
                from t in ctx.GLTransactions.AsNoTracking()
                join a in ctx.SegChartOfAccounts.AsNoTracking() on t.SegCoaId equals a.Id
                where t.CompanyId == companyId && t.AccountingPeriodId == periodId
                group t by new { t.SegCoaId, a.AccountCode, a.Description } into g
                orderby g.Key.AccountCode
                select new TrialBalanceRow
                {
                    SegCoaId = g.Key.SegCoaId,
                    AccountCode = g.Key.AccountCode,
                    AccountName = g.Key.Description,
                    TotalDebit = g.Sum(x => x.Debit),
                    TotalCredit = g.Sum(x => x.Credit)
                };

            return await query.ToListAsync();
        }

        public async Task<List<LedgerReportRow>> GetLedgerReportAsync(Guid companyId, DateOnly startDate, DateOnly endDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var query =
                from t in ctx.GLTransactions.AsNoTracking()
                join a in ctx.SegChartOfAccounts.AsNoTracking() on t.SegCoaId equals a.Id
                join j in ctx.GLJournalHeaders.AsNoTracking() on t.JournalId equals j.Id
                where t.CompanyId == companyId
                      && t.PostingDate >= startDate
                      && t.PostingDate <= endDate
                orderby a.AccountCode, t.PostingDate
                select new LedgerReportRow
                {
                    SegCoaId = t.SegCoaId,
                    AccountCode = a.AccountCode,
                    AccountName = a.Description,
                    PostingDate = t.PostingDate,
                    JournalNumber = j.JournalNumber,
                    Narration = t.Narration,
                    Debit = t.Debit,
                    Credit = t.Credit
                };

            return await query.ToListAsync();
        }

        // =========================================================
        // 6) Post Batch (Atomic, Segmented)
        // =========================================================
        public async Task<string> PostBatchAsync(Guid companyId, Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var batch = await ctx.GLBatches
                    .Include(b => b.Journals)
                    .ThenInclude(j => j.Lines)
                    .FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);

                if (batch == null) return "Batch not found.";

                // Idempotent already posted
                if (batch.Status == BatchStatus.Posted)
                    return "Batch is already posted.";

                // Auto-release draft batches
                if (batch.Status == BatchStatus.Draft)
                {
                    foreach (var j in batch.Journals)
                        if (!IsBalanced(j.Lines)) return $"Journal '{j.JournalNumber}' is not balanced. Cannot auto-post.";

                    batch.Status = BatchStatus.Ready;
                    batch.ReleasedByUserId = "SYSTEM_AUTO";
                    batch.ReleasedAt = DateTime.UtcNow;

                    await ctx.SaveChangesAsync();
                }

                if (batch.Status != BatchStatus.Ready)
                    return $"Batch cannot be posted. Current Status: {batch.Status}";

                // Validate accounts once per batch before writing transactions
                var allLines = batch.Journals.SelectMany(j => j.Lines).ToList();
                var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, allLines, requireAllowJournal: false);
                if (!string.IsNullOrWhiteSpace(acctErr)) return acctErr!;

                foreach (var journal in batch.Journals)
                {
                    if (!IsBalanced(journal.Lines))
                        throw new InvalidOperationException("Journal unbalanced.");

                    foreach (var line in journal.Lines)
                    {
                        ctx.GLTransactions.Add(new GLTransaction
                        {
                            CompanyId = companyId,
                            AccountingPeriodId = batch.AccountingPeriodId,
                            PostingDate = journal.TransactionDate,
                            BatchId = batch.Id,
                            JournalId = journal.Id,
                            SegCoaId = line.SegCoaId,
                            Debit = line.Debit,
                            Credit = line.Credit,
                            Narration = line.Reference ?? journal.Narration,
                        });
                    }

                    journal.Status = JournalStatus.Posted;
                }

                batch.Status = BatchStatus.Posted;
                batch.PostedByUserId = "SYSTEM_AUTO";
                batch.PostedAt = DateTime.UtcNow;

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Posting failed: {ex.Message}";
            }
        }
    }
}
