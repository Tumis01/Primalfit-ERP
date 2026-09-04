using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class CashbookService : ICashbookService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;

        public CashbookService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
        }

        private static void AddAudit(AppDbContext ctx, Guid companyId, string action, Guid entityId, string details)
        {
            ctx.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId, UserId = "system", Action = action,
                EntityType = nameof(CashbookBatch), EntityId = entityId,
                Details = details, CreatedAt = DateTime.UtcNow
            });
        }

        public async Task<List<CashbookBatch>> GetActiveBatchesAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batches = await ctx.CashbookBatches
                .Include(b => b.Entries)
                .Where(b => b.CompanyId == companyId)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();

            var glBatchIds = batches.Where(b => b.PostedGLBatchId.HasValue)
                .Select(b => b.PostedGLBatchId!.Value).ToList();
            var glStatuses = await ctx.GLBatches.AsNoTracking()
                .Where(b => glBatchIds.Contains(b.Id) && b.CompanyId == companyId)
                .ToDictionaryAsync(b => b.Id, b => new { b.Status, b.RejectionReason });

            foreach (var batch in batches)
            {
                if (batch.PostedGLBatchId.HasValue && glStatuses.TryGetValue(batch.PostedGLBatchId.Value, out var gl))
                {
                    if (gl.Status == BatchStatus.Rejected)
                    {
                        batch.Status = BatchStatus.Rejected;
                        batch.IsLocked = false;
                        batch.RejectionReason = gl.RejectionReason;
                    }
                    else if (gl.Status == BatchStatus.Posted)
                    {
                        batch.Status = BatchStatus.Posted;
                        batch.IsLocked = true;
                        if (batch.ClearAfterPost)
                        {
                            ctx.CashbookEntries.RemoveRange(batch.Entries);
                            batch.Entries.Clear();
                        }
                        else foreach (var entry in batch.Entries) entry.IsPosted = true;
                    }
                }
            }

            await ctx.SaveChangesAsync();
            return batches;
        }

        public async Task<CashbookBatch> GetBatchByIdAsync(Guid id)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch != null && batch.PostedGLBatchId.HasValue)
            {
                var glBatch = await ctx.GLBatches.AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == batch.PostedGLBatchId.Value && g.CompanyId == batch.CompanyId);
                if (glBatch?.Status == BatchStatus.Rejected)
                {
                    batch.Status = BatchStatus.Rejected;
                    batch.IsLocked = false;
                    batch.RejectionReason = glBatch.RejectionReason;
                }
                else if (glBatch?.Status == BatchStatus.Posted)
                {
                    batch.Status = BatchStatus.Posted;
                    batch.IsLocked = true;
                    if (batch.ClearAfterPost)
                    {
                        ctx.CashbookEntries.RemoveRange(batch.Entries);
                        batch.Entries.Clear();
                    }
                    else foreach (var entry in batch.Entries) entry.IsPosted = true;
                }
            }

            if (batch != null)
            {
                batch.TotalDebits = batch.Entries.Sum(e => e.Debit);
                batch.TotalCredits = batch.Entries.Sum(e => e.Credit);
            }

            return batch!;
        }

        public async Task<CashbookBatch> CreateBatchAsync(Guid companyId, Guid bankAccountId, string userId, bool isForeign, Guid? currencyId, decimal exchangeRate, bool clearAfterPost)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var bank = await ctx.SegChartOfAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == bankAccountId && x.IsActive);

            if (bank == null)
                throw new InvalidOperationException("Selected bank account does not exist or is Inactive.");

            decimal openingBal = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId && t.SegCoaId == bankAccountId)
                .SumAsync(t => t.Debit - t.Credit);

            var batch = new CashbookBatch
            {
                CompanyId = companyId,
                BankSegCoaId = bankAccountId,
                BatchReference = $"CB-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(100, 999)}",
                CreatedByUserId = userId,
                OpeningBalance = openingBal,
                Status = BatchStatus.Draft,
                // Map the new fields
                IsForeignCurrency = isForeign,
                CurrencyId = isForeign ? currencyId : null,
                ExchangeRate = isForeign && exchangeRate > 0 ? exchangeRate : 1,
                ClearAfterPost = clearAfterPost
            };

            ctx.CashbookBatches.Add(batch);
            await ctx.SaveChangesAsync();

            return batch;
        }

        public async Task<string> AddEntryAsync(CashbookEntry entry)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            if (entry.Id == Guid.Empty) entry.Id = Guid.NewGuid();
            var batch = await ctx.CashbookBatches.FirstOrDefaultAsync(b => b.Id == entry.CashbookBatchId);
            if (batch == null) return "Cashbook batch not found.";
            if (batch.Status != BatchStatus.Draft || batch.IsLocked) return "Cashbook batch is locked.";
            if (entry.OffsetSegCoaId == Guid.Empty) return "Offset account is required.";
            if (entry.Debit <= 0 && entry.Credit <= 0) return "Enter a Debit or Credit amount.";

            ctx.CashbookEntries.Add(entry);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> UpdateEntryAsync(CashbookEntry entry)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var existing = await ctx.CashbookEntries.FindAsync(entry.Id);
            if (existing == null) return "Entry not found.";
            var batch = await ctx.CashbookBatches.FirstOrDefaultAsync(b => b.Id == existing.CashbookBatchId);
            if (batch == null || batch.Status != BatchStatus.Draft || batch.IsLocked) return "Cashbook batch is locked.";
            if (existing.IsPosted) return "Posted cashbook entries cannot be edited.";

            if (entry.OffsetSegCoaId == Guid.Empty) return "Offset account is required.";
            if (entry.Debit <= 0 && entry.Credit <= 0) return "Enter a Debit or Credit amount.";

            ctx.Entry(existing).CurrentValues.SetValues(entry);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task RemoveEntryAsync(Guid id)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var entry = await ctx.CashbookEntries.FindAsync(id);
            if (entry == null) return;
            var batch = await ctx.CashbookBatches.FirstOrDefaultAsync(b => b.Id == entry.CashbookBatchId);
            if (batch == null || batch.Status != BatchStatus.Draft || batch.IsLocked || entry.IsPosted) return;

            ctx.CashbookEntries.Remove(entry);
            await ctx.SaveChangesAsync();
        }

        public async Task<string> SubmitForApprovalAsync(Guid batchId)
        {
            return await PostBatchAsync(batchId, "system");
        }

        public async Task<string> RevertToDraftAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches.FirstOrDefaultAsync(b => b.Id == batchId);
            if (batch == null) return "Batch not found.";

            // Prevent unlocking if the central reviewer already posted the batch
            if (batch.PostedGLBatchId.HasValue)
            {
                var glBatch = await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(g => g.Id == batch.PostedGLBatchId.Value);
                if (glBatch != null && glBatch.Status == BatchStatus.Posted)
                {
                    return "This batch has already been committed to the General Ledger by an approver and cannot be unlocked.";
                }
            }

            if (batch.Status == BatchStatus.Posted) return "Use Reuse Batch to begin a new posting cycle.";
            batch.Status = BatchStatus.Draft;
            batch.IsLocked = false;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> LockBatchAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.CashbookBatches.FirstOrDefaultAsync(b => b.Id == batchId);
            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be locked.";
            if (batch.IsLocked) return "Batch is already locked.";
            batch.IsLocked = true;
            AddAudit(ctx, batch.CompanyId, "CashbookBatchLocked", batch.Id,
                $"Cashbook batch '{batch.BatchReference}' was locked as Draft.");
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> ReuseBatchAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.CashbookBatches.FirstOrDefaultAsync(b => b.Id == batchId);
            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Posted) return "Only posted batches can be reused.";
            batch.Status = BatchStatus.Draft;
            batch.IsLocked = false;
            batch.PostedGLBatchId = null;
            batch.RejectionReason = null;
            AddAudit(ctx, batch.CompanyId, "CashbookBatchReused", batch.Id,
                $"Posted cashbook batch '{batch.BatchReference}' was reopened for additional entries.");
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> PostBatchAsync(Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft && batch.Status != BatchStatus.Ready && batch.Status != BatchStatus.Rejected)
                return "Batch cannot be staged in its current status.";

            var pendingEntries = batch.Entries.Where(e => !e.IsPosted).ToList();
            if (!pendingEntries.Any()) return "No entries to stage.";

            var postingDate = DateOnly.FromDateTime(pendingEntries.Max(e => e.TransactionDate));

            // Validate accounts
            var allCoaIds = pendingEntries.Select(e => e.OffsetSegCoaId).ToList();
            allCoaIds.Add(batch.BankSegCoaId);
            allCoaIds = allCoaIds.Where(x => x != Guid.Empty).Distinct().ToList();

            var existingIds = await ctx.SegChartOfAccounts
                .AsNoTracking()
                .Where(a => a.CompanyId == batch.CompanyId && allCoaIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync();

            if (existingIds.Count != allCoaIds.Count)
                return "One or more selected accounts do not exist in Segmented COA.";

            var glLines = new List<GLJournalLine>();

            foreach (var entry in pendingEntries)
            {
                if (entry.OffsetSegCoaId == Guid.Empty) return "One or more entries are missing an offset account.";
                if (entry.Debit <= 0 && entry.Credit <= 0) return "One or more entries have zero amount.";

                // Debit Bank / Credit Offset or vice-versa
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = entry.OffsetSegCoaId,
                    Debit = entry.Credit,
                    Credit = entry.Debit,
                    Reference = $"{entry.Reference}: {entry.Description}"
                });

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = batch.BankSegCoaId,
                    Debit = entry.Debit,
                    Credit = entry.Credit,
                    Reference = $"Cashbook: {entry.Reference}"
                });
            }

            // Stage the batch to the central GL router without creating GLTransactions
            var (err, glBatchId) = await _glOps.StageSubledgerBatchAsync(
                companyId: batch.CompanyId,
                txnDate: postingDate,
                batchName: batch.BatchReference,
                description: $"Cashbook: {batch.BatchReference}",
                sourceReference: batch.BatchReference,
                lines: glLines,
                userId: userId,
                existingBatchId: batch.PostedGLBatchId,
                clearAfterPost: batch.ClearAfterPost
            );

            if (!string.IsNullOrWhiteSpace(err)) return err;

            batch.PostedGLBatchId = glBatchId;
            batch.Status = BatchStatus.Ready; // Staged for review on /gl/batches
            batch.IsLocked = true;
            AddAudit(ctx, batch.CompanyId, "CashbookBatchSubmittedForReview", batch.Id,
                $"Cashbook batch '{batch.BatchReference}' was staged for General Ledger review.");

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> FinalizeApprovedBatchAsync(Guid glBatchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.CashbookBatches.Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.PostedGLBatchId == glBatchId);
            if (batch == null) return string.Empty;

            var glStatus = await ctx.GLBatches.AsNoTracking()
                .Where(g => g.Id == glBatchId && g.CompanyId == batch.CompanyId)
                .Select(g => g.Status)
                .FirstOrDefaultAsync();
            if (glStatus != BatchStatus.Posted) return "The linked General Ledger batch is not posted.";

            batch.Status = BatchStatus.Posted;
            batch.IsLocked = true;
            if (batch.ClearAfterPost)
            {
                ctx.CashbookEntries.RemoveRange(batch.Entries);
                batch.Entries.Clear();
            }
            else
            {
                foreach (var entry in batch.Entries) entry.IsPosted = true;
            }
            AddAudit(ctx, batch.CompanyId, "CashbookBatchApprovedAndPosted", batch.Id,
                $"Cashbook batch '{batch.BatchReference}' was finalized after GL approval by {userId}.");
            await ctx.SaveChangesAsync();
            return string.Empty;
        }


        public async Task<string> DeleteDraftBatchAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches
                .Include(b => b.Entries) // Include entries to delete them safely
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only draft batches can be deleted.";
            if (batch.IsLocked) return "Unlock the batch before deleting it.";

            // Explicitly remove entries first to prevent Foreign Key constraint errors
            if (batch.Entries.Any())
            {
                ctx.CashbookEntries.RemoveRange(batch.Entries);
            }

            // Remove the batch itself
            ctx.CashbookBatches.Remove(batch);

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}
