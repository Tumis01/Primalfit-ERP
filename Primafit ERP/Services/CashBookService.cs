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

        public async Task<List<CashbookBatch>> GetActiveBatchesAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.CashbookBatches
                .Include(b => b.Entries)
                .Where(b => b.CompanyId == companyId && b.Status != BatchStatus.Posted)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();
        }

        public async Task<CashbookBatch> GetBatchByIdAsync(Guid id)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch != null)
            {
                batch.TotalDebits = batch.Entries.Sum(e => e.Debit);
                batch.TotalCredits = batch.Entries.Sum(e => e.Credit);
            }

            return batch!;
        }

        public async Task<CashbookBatch> CreateBatchAsync(Guid companyId, Guid bankAccountId, string userId)
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
                Status = BatchStatus.Draft
            };

            ctx.CashbookBatches.Add(batch);
            await ctx.SaveChangesAsync();

            return batch;
        }

        public async Task<string> AddEntryAsync(CashbookEntry entry)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            if (entry.Id == Guid.Empty) entry.Id = Guid.NewGuid();
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

            ctx.CashbookEntries.Remove(entry);
            await ctx.SaveChangesAsync();
        }

        public async Task<string> SubmitForApprovalAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches.FindAsync(batchId);
            if (batch == null) return "Batch not found.";

            batch.Status = BatchStatus.Ready;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RevertToDraftAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches.FindAsync(batchId);
            if (batch == null) return "Batch not found.";

            batch.Status = BatchStatus.Draft;
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
            if (batch.Status != BatchStatus.Ready) return "Batch must be locked (Ready) before posting.";
            if (!batch.Entries.Any()) return "Cannot post empty batch.";
            if (batch.PostedGLBatchId.HasValue) return "Batch already posted to GL.";

            var postingDate = DateOnly.FromDateTime(batch.Entries.Max(e => e.TransactionDate));

            var allCoaIds = batch.Entries.Select(e => e.OffsetSegCoaId).ToList();
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

            // RULE 3 & 4: Balance each account individually against the bank (No cumulative totals)
            foreach (var entry in batch.Entries)
            {
                if (entry.OffsetSegCoaId == Guid.Empty)
                    return "One or more entries are missing an offset account.";

                if (entry.Debit <= 0 && entry.Credit <= 0)
                    return "One or more entries have zero amount.";

                // 1. The Offset Account Line (Gets exactly what the user typed in the UI)
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = entry.OffsetSegCoaId,
                    Debit = entry.Debit,
                    Credit = entry.Credit,
                    Reference = $"{entry.Reference}: {entry.Description}",
                });

                // 2. The Bank Account Line (Immediately perfectly balances the offset)
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = batch.BankSegCoaId,
                    Debit = entry.Credit, // Opposite of Offset
                    Credit = entry.Debit, // Opposite of Offset
                    Reference = $"Cashbook: {entry.Reference}"
                });
            }

            var (err, glBatchId) = await _glOps.CreateJournalEntryAsync(
                batch.CompanyId,
                postingDate,
                "Cashbook Posting",
                $"Ref: {batch.BatchReference}",
                glLines);

            if (!string.IsNullOrWhiteSpace(err)) return err;

            batch.Status = BatchStatus.Posted;
            batch.PostedGLBatchId = glBatchId;
            await ctx.SaveChangesAsync();

            if (glBatchId.HasValue)
            {
                var postErr = await _glOps.PostBatchAsync(batch.CompanyId, glBatchId.Value);
                if (!string.IsNullOrWhiteSpace(postErr))
                    return $"Cashbook marked as posted but GL posting failed: {postErr}";
            }

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