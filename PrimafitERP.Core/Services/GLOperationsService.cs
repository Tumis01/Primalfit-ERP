using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            var period = await ctx.AccountingPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId
                                       && !p.IsClosed
                                       && p.StartDate <= txnDate
                                       && p.EndDate >= txnDate);

            if (period != null) return period;

            var openPeriods = await ctx.AccountingPeriods
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && !p.IsClosed)
                .OrderBy(p => p.StartDate)
                .Select(p => $"{p.StartDate:MMM dd, yyyy} to {p.EndDate:MMM dd, yyyy}")
                .ToListAsync();

            if (!openPeriods.Any())
            {
                throw new Exception("Cannot process transaction: There are no open accounting periods available for this company. Please open a new period in the Ledger settings.");
            }

            var availableRanges = string.Join(" | ", openPeriods);

            throw new Exception($"Cannot process transaction: The date {txnDate:MMM dd, yyyy} is closed or invalid. Please select a date within the following open periods: {availableRanges}");
        }

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
    List<GLJournalLine> lines,
    string userId,
    bool requireAllowJournal = false) // FIXED: Default to false to allow automated sub-ledgers to pass control accounts
        {
            string journalNumber = $"JV-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

            // Pass the bypass configuration down into the batch engine context
            var result = await CreateDraftBatchAsync(
                companyId,
                txnDate,
                batchName,
                description,
                journalNumber,
                description,
                lines,
                BatchType.Standard,
                userId,
                requireAllowJournal); // FIXED: Forwarding the configuration flag

            if (!string.IsNullOrWhiteSpace(result.error) || !result.batchId.HasValue)
                return result;

            var releaseErr = await ReleaseBatchAsync(companyId, result.batchId.Value, userId);
            if (!string.IsNullOrWhiteSpace(releaseErr))
                return (releaseErr, result.batchId);

            return result;
        }

        // =========================================================
        // 2) CORE: Create Draft Batch (The Engine)
        // =========================================================
        public async Task<(string error, Guid? batchId)> CreateDraftBatchAsync(
            Guid companyId, DateOnly txnDate, string batchName, string? description,
            string journalNumber, string? narration, List<GLJournalLine> lines,
            BatchType type, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var cleanLines = lines
                .Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0))
                .ToList();

            if (cleanLines.Count == 0) return ("No valid lines.", null);

            if (type == BatchType.Standard && !IsBalanced(cleanLines))
                return ("Journal is not balanced (Debits must equal Credits).", null);

            AccountingPeriod period;
            try { period = await ResolvePeriodOrThrow(ctx, companyId, txnDate); }
            catch (Exception ex) { return (ex.Message, null); }

            // FIX: Only enforce Allow Journal rules for Standard Manual Journals
            bool requireAllowJournal = type == BatchType.Standard;

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
                CreatedByUserId = userId,
                ClearAfterPost = false
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
        public async Task<(string error, Guid? batchId)> CreateDraftBatchAsync(
    Guid companyId,
    DateOnly txnDate,
    string batchName,
    string? description,
    string journalNumber,
    string? narration,
    List<GLJournalLine> lines,
    BatchType type,
    string userId,
    bool? requireAllowJournalOverride = null)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var cleanLines = lines
                .Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0))
                .ToList();

            if (cleanLines.Count == 0) return ("No valid lines.", null);

            if (type == BatchType.Standard && !IsBalanced(cleanLines))
                return ("Journal is not balanced (Debits must equal Credits).", null);

            AccountingPeriod period;
            try { period = await ResolvePeriodOrThrow(ctx, companyId, txnDate); }
            catch (Exception ex) { return (ex.Message, null); }

            // FIXED: Evaluate if an explicit sub-ledger bypass override flag is passed down
            bool requireAllowJournal = requireAllowJournalOverride ?? (type == BatchType.Standard);

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
                CreatedByUserId = userId,
                ClearAfterPost = false
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
        public async Task<string> CreateOpeningBalanceMigrationAsync(Guid companyId, DateOnly migrationDate, string batchName, List<GLJournalLine> inputLines, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var cleanLines = inputLines.Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0)).ToList();
            if (cleanLines.Count == 0) return "Enter at least one valid line.";

            AccountingPeriod period;
            try { period = await ResolvePeriodOrThrow(ctx, companyId, migrationDate); }
            catch (Exception ex) { return ex.Message; }

            // FIX: Allow Migration batches to post to Control Accounts (AR, AP, Inventory)
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, cleanLines, requireAllowJournal: false);
            if (!string.IsNullOrWhiteSpace(acctErr)) return acctErr!;

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = "System Migration",
                Type = BatchType.Migration,
                Status = BatchStatus.Draft,
                CreatedByUserId = userId
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
        // 4) Release & Reject
        // =========================================================
        public async Task<string> ReleaseBatchAsync(Guid companyId, Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).ThenInclude(j => j.Lines).FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);
            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be released.";

            foreach (var j in batch.Journals)
                if (!IsBalanced(j.Lines)) return $"Journal '{j.JournalNumber}' is not balanced.";

            batch.Status = BatchStatus.Ready;
            batch.ReleasedByUserId = userId; // Assigned to actual user
            batch.ReleasedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RejectBatchAsync(Guid companyId, Guid batchId, string reason, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);
            if (batch == null) return "Batch not found.";

            batch.Status = BatchStatus.Rejected;
            batch.RejectedByUserId = userId; // Assigned to actual user
            batch.RejectedAt = DateTime.UtcNow;
            batch.RejectionReason = reason;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // =========================================================
        // 5) Post Batch (Atomic, Segmented)
        // =========================================================
        public async Task<string> PostBatchAsync(Guid companyId, Guid batchId, string userId)
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
                if (batch.Status != BatchStatus.Ready) return $"Batch cannot be posted. Current Status: {batch.Status}";

                var targetPeriod = await ctx.AccountingPeriods
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == batch.AccountingPeriodId);

                if (targetPeriod == null) return "Fatal Error: The accounting period linked to this batch no longer exists.";
                if (targetPeriod.IsClosed) return $"STOP: Cannot post. The accounting period ({targetPeriod.StartDate:yyyy-MM-dd} to {targetPeriod.EndDate:yyyy-MM-dd}) is currently CLOSED.";

                foreach (var journal in batch.Journals)
                {
                    // Only validate and post the UNPOSTED lines
                    var pendingLines = journal.Lines.Where(l => !l.IsPosted).ToList();

                    if (!pendingLines.Any()) return "No new lines to post.";
                    if (!IsBalanced(pendingLines)) return $"Journal '{journal.JournalNumber}' pending lines are not balanced.";

                    var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, pendingLines, requireAllowJournal: false);
                    if (!string.IsNullOrWhiteSpace(acctErr)) return acctErr!;

                    foreach (var line in pendingLines)
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

                        if (batch.ClearAfterPost)
                            ctx.Set<GLJournalLine>().Remove(line);
                        else
                            line.IsPosted = true;
                    }
                }
                if (batch.BatchName.StartsWith("JV-"))
                {
                    batch.Status = BatchStatus.Draft;
                }
                else
                {
                    batch.Status = BatchStatus.Posted;
                }

                batch.PostedByUserId = userId;
                batch.PostedAt = DateTime.UtcNow;

                // NOTE: I removed the duplicate lines here that were 
                // forcefully overriding the status back to Posted!

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

        public async Task<List<GLBatch>> GetActiveJournalBatchesAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.GLBatches
                .Include(b => b.Journals)
                .ThenInclude(j => j.Lines)
                .Where(b => b.CompanyId == companyId
                         && b.Type == BatchType.Standard
                         && b.BatchName.StartsWith("JV-") // Strictly isolates manual journals
                         && (b.Status == BatchStatus.Draft ||
                             b.Status == BatchStatus.Ready ||
                             (b.Status == BatchStatus.Posted && !b.ClearAfterPost))) // Show retained posted batches
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        // =========================================================
        // 6) INTERACTIVE UI LIFECYCLE
        // =========================================================

        public async Task<GLBatch?> GetBatchByIdAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.GLBatches
                .Include(b => b.Journals)
                .ThenInclude(j => j.Lines)
                .FirstOrDefaultAsync(b => b.Id == batchId);
        }

        public async Task<GLBatch> CreateDraftBatchAsync(
            Guid companyId, DateOnly date, string description, string userId, bool clearAfterPost)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var period = await ResolvePeriodOrThrow(ctx, companyId, date);

            var batchName = $"JV-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = description,
                Type = BatchType.Standard,
                Status = BatchStatus.Draft,
                CreatedByUserId = userId,
                ClearAfterPost = clearAfterPost
            };

            batch.Journals.Add(new GLJournalHeader
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                JournalNumber = batchName,
                Narration = description,
                TransactionDate = date,
                Status = JournalStatus.Draft
            });

            ctx.GLBatches.Add(batch);
            await ctx.SaveChangesAsync();
            return batch;
        }

        // Notice the query no longer explicitly ignores Anonymous, 
        // as we are now assuming all Standard batches originate from a real user ID.
        

        public async Task<string> DeleteDraftBatchAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).ThenInclude(j => j.Lines).FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be deleted.";

            ctx.GLBatches.Remove(batch);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> SubmitForApprovalAsync(Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).ThenInclude(j => j.Lines).FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be locked.";

            var allLines = batch.Journals.SelectMany(j => j.Lines).ToList();
            if (!allLines.Any()) return "Batch has no lines. Cannot lock.";

            foreach (var j in batch.Journals)
                if (!IsBalanced(j.Lines)) return $"Journal '{j.JournalNumber}' is not balanced.";

            batch.Status = BatchStatus.Ready;
            batch.ReleasedByUserId = userId; // Log who submitted it
            batch.ReleasedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RevertToDraftAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status == BatchStatus.Posted) return "Cannot unlock a posted batch.";

            batch.Status = BatchStatus.Draft;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> AddJournalLineAsync(Guid batchId, GLJournalLine line)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Batch is locked.";

            var header = batch.Journals.FirstOrDefault();
            if (header == null) return "Journal Header is missing.";

            // FIX: Only enforce if this is a Standard Batch
            bool requireAllowJournal = batch.Type == BatchType.Standard;
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, batch.CompanyId, new List<GLJournalLine> { line }, requireAllowJournal);

            if (!string.IsNullOrEmpty(acctErr)) return acctErr;

            line.HeaderId = header.Id;
            ctx.Set<GLJournalLine>().Add(line);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> UpdateJournalLineAsync(GLJournalLine line)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.Set<GLJournalLine>().Include(l => l.Header).ThenInclude(h => h.Batch).FirstOrDefaultAsync(l => l.Id == line.Id);

            if (existing == null) return "Line not found.";
            if (existing.Header?.Batch?.Status != BatchStatus.Draft) return "Batch is locked.";

            // FIX: Only enforce if this is a Standard Batch
            bool requireAllowJournal = existing.Header.Batch.Type == BatchType.Standard;
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, existing.Header.CompanyId, new List<GLJournalLine> { line }, requireAllowJournal);

            if (!string.IsNullOrEmpty(acctErr)) return acctErr;

            existing.SegCoaId = line.SegCoaId;
            existing.Reference = line.Reference;
            existing.Debit = line.Debit;
            existing.Credit = line.Credit;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RemoveJournalLineAsync(Guid lineId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.Set<GLJournalLine>().Include(l => l.Header).ThenInclude(h => h.Batch).FirstOrDefaultAsync(l => l.Id == lineId);

            if (existing == null) return "Line not found.";
            if (existing.Header?.Batch?.Status != BatchStatus.Draft) return "Batch is locked.";

            ctx.Set<GLJournalLine>().Remove(existing);
            await ctx.SaveChangesAsync();
            return string.Empty;
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
    }
}