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

        // Helpers
        private static bool IsBalanced(IEnumerable<GLJournalLine> lines) => lines.Sum(x => x.Debit) == lines.Sum(x => x.Credit);

        private async Task<AccountingPeriod> ResolvePeriodOrThrow(AppDbContext ctx, Guid companyId, DateOnly txnDate)
        {
            // 1. Fetch all open periods for the company (Client-side evaluation is safer for DateOnly comparisons in some EF versions)
            var periods = await ctx.AccountingPeriods
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && !p.IsClosed)
                .ToListAsync();

            // 2. Perform the check in memory to guarantee exact DateOnly matching
            var period = periods.FirstOrDefault(p => p.StartDate <= txnDate && p.EndDate >= txnDate);

            if (period == null)
            {
                // Debugging Aid: Show the user what dates were checked
                var availableRanges = string.Join(", ", periods.Select(p => $"{p.StartDate:yyyy-MM-dd} to {p.EndDate:yyyy-MM-dd}"));
                throw new InvalidOperationException($"No active accounting period found for {txnDate:yyyy-MM-dd}. Open periods are: [{availableRanges}]");
            }

            return period;
        }

        private async Task WriteAudit(AppDbContext ctx, Guid companyId, string action, string entityType, Guid entityId, string? details = null)
        {
            ctx.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = "ANONYMOUS_USER",
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        // =========================================================
        // 1. ADDED: Create Standard Journal Entry (Wrapper)
        // =========================================================
        public async Task<(string error, Guid? batchId)> CreateJournalEntryAsync(
            Guid companyId,
            DateOnly txnDate,
            string batchName,
            string? description,
            List<GLJournalLine> lines)
        {
            // Auto-generate a unique Journal Number
            string journalNumber = $"JV-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";

            // Call the main engine
            return await CreateDraftBatchAsync(
                companyId,
                txnDate,
                batchName,
                description,
                journalNumber,
                description, // Narration
                lines,
                BatchType.Standard // Type
            );
        }

        // =========================================================
        // 2. CORE: Create Draft Batch (The Engine)
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

            // Validate Lines
            var cleanLines = lines.Where(l => l.AccountId != Guid.Empty && (l.Debit > 0 || l.Credit > 0)).ToList();
            if (cleanLines.Count == 0) return ("No valid lines.", null);

            // Validate Balance (Standard Journals must be balanced immediately)
            if (type == BatchType.Standard && !IsBalanced(cleanLines))
            {
                return ("Journal is not balanced (Debits must equal Credits).", null);
            }

            // Validate Period
            AccountingPeriod period;
            try
            {
                period = await ResolvePeriodOrThrow(ctx, companyId, txnDate);
            }
            catch (Exception ex)
            {
                return (ex.Message, null);
            }

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

        // 3) Opening Balances
        public async Task<string> CreateOpeningBalanceMigrationAsync(Guid companyId, DateOnly migrationDate, string batchName, List<GLJournalLine> inputLines)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var cleanLines = inputLines.Where(l => l.AccountId != Guid.Empty && (l.Debit > 0 || l.Credit > 0)).ToList();
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

        // 4) Release
        public async Task<string> ReleaseBatchAsync(Guid companyId, Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).ThenInclude(j => j.Lines)
                .FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be released.";

            // Balance Check
            foreach (var j in batch.Journals)
                if (!IsBalanced(j.Lines)) return $"Journal '{j.JournalNumber}' is not balanced.";

            batch.Status = BatchStatus.Ready;
            batch.ReleasedByUserId = "ANONYMOUS_USER";
            batch.ReleasedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 5) Reject
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
        public async Task<List<TrialBalanceRow>> GetTrialBalanceAsync(Guid companyId, Guid periodId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var query = from t in ctx.GLTransactions.AsNoTracking()
                        join a in ctx.GLChartOfAccounts.AsNoTracking() on t.AccountId equals a.Id
                        where t.CompanyId == companyId && t.AccountingPeriodId == periodId
                        group t by new { t.AccountId, a.AccountCode, a.AccountName } into g
                        orderby g.Key.AccountCode
                        select new TrialBalanceRow
                        {
                            AccountId = g.Key.AccountId,
                            AccountCode = g.Key.AccountCode,
                            AccountName = g.Key.AccountName,
                            TotalDebit = g.Sum(x => x.Debit),
                            TotalCredit = g.Sum(x => x.Credit)
                        };

            return await query.ToListAsync();
        }
        public async Task<string> PostJournalAsync(GLJournalHeader journal)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Validation: Double-Entry Accounting Rule
            // Total Debits MUST equal Total Credits
            decimal totalDebit = journal.Lines.Sum(l => l.Debit);
            decimal totalCredit = journal.Lines.Sum(l => l.Credit);

            // Allow for tiny floating point differences if needed, but 'decimal' usually handles this exactly
            if (totalDebit != totalCredit)
            {
                return $"Journal Posting Failed: Imbalance detected. Total Debit ({totalDebit:N2}) != Total Credit ({totalCredit:N2})";
            }

            // 2. Ensure IDs and Links are set
            if (journal.Id == Guid.Empty) journal.Id = Guid.NewGuid();

            foreach (var line in journal.Lines)
            {
                if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();

                // Ensure foreign key link is established
                // (Assuming your JournalLine model has a MasterJournalId property)
                line.Id = journal.Id;
            }

            // 3. Commit to Database
            try
            {
                ctx.GLJournalHeaders.Add(journal);
                await ctx.SaveChangesAsync();
                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                // Capture inner exception for details like Foreign Key errors
                var msg = ex.InnerException?.Message ?? ex.Message;
                return $"GL Database Error: {msg}";
            }
        }

        // 6) Post (Atomic)
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

                // --- FIX: AUTO-RELEASE IF DRAFT ---
                if (batch.Status == BatchStatus.Draft)
                {
                    // 1. Check Balance before auto-releasing
                    foreach (var j in batch.Journals)
                    {
                        if (!IsBalanced(j.Lines)) return $"Journal '{j.JournalNumber}' is not balanced. Cannot auto-post.";
                    }

                    // 2. Promote to Ready automatically
                    batch.Status = BatchStatus.Ready;
                    batch.ReleasedByUserId = "SYSTEM_AUTO";
                    batch.ReleasedAt = DateTime.UtcNow;

                    // Save this state transition so if posting fails later, it's at least "Ready"
                    await ctx.SaveChangesAsync();
                }
                // ----------------------------------

                if (batch.Status != BatchStatus.Ready) return "Batch must be Ready (Released) before posting.";

                // PROCEED WITH POSTING (Move to GLTransactions)
                foreach (var journal in batch.Journals)
                {
                    // Double check balance (sanity check)
                    if (!IsBalanced(journal.Lines)) throw new InvalidOperationException($"Journal unbalanced.");

                    foreach (var line in journal.Lines)
                    {
                        ctx.GLTransactions.Add(new GLTransaction
                        {
                            CompanyId = companyId,
                            AccountingPeriodId = batch.AccountingPeriodId,
                            PostingDate = journal.TransactionDate,
                            BatchId = batch.Id,
                            JournalId = journal.Id,
                            AccountId = line.AccountId,
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
                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Posting failed: {ex.Message}";
            }
        }
    }
}