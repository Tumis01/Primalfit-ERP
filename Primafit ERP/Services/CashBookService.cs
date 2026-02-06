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
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.CashbookBatches
                .Include(b => b.Entries)
                .Where(b => b.CompanyId == companyId && b.Status != BatchStatus.Posted)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();
        }

        public async Task<CashbookBatch> GetBatchByIdAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.CashbookBatches
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == id);

            // Recalculate totals on load (safety)
            if (batch != null)
            {
                batch.TotalDebits = batch.Entries.Sum(e => e.Debit);
                batch.TotalCredits = batch.Entries.Sum(e => e.Credit);
            }
            return batch;
        }

        public async Task<CashbookBatch> CreateBatchAsync(Guid companyId, Guid bankAccountId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Get Opening Balance (Running Balance of the GL Account)
            decimal openingBal = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId && t.AccountId == bankAccountId)
                .SumAsync(t => t.Debit - t.Credit);

            var batch = new CashbookBatch
            {
                CompanyId = companyId,
                BankAccountId = bankAccountId,
                BatchReference = $"CB-{DateTime.UtcNow:yyMMdd}-{new Random().Next(100, 999)}",
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
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (entry.Id == Guid.Empty) entry.Id = Guid.NewGuid();

            ctx.CashbookEntries.Add(entry);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> UpdateEntryAsync(CashbookEntry entry)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.CashbookEntries.FindAsync(entry.Id);
            if (existing == null) return "Entry not found.";

            ctx.Entry(existing).CurrentValues.SetValues(entry);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task RemoveEntryAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var entry = await ctx.CashbookEntries.FindAsync(id);
            if (entry != null)
            {
                ctx.CashbookEntries.Remove(entry);
                await ctx.SaveChangesAsync();
            }
        }

        public async Task<string> SubmitForApprovalAsync(Guid batchId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.CashbookBatches.FindAsync(batchId);
            if (batch == null) return "Batch not found.";

            batch.Status = BatchStatus.Ready; // "Lock" the batch
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RevertToDraftAsync(Guid batchId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.CashbookBatches.FindAsync(batchId);
            if (batch == null) return "Batch not found.";

            batch.Status = BatchStatus.Draft;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // --- THE CRITICAL FIX IS HERE ---
        public async Task<string> PostBatchAsync(Guid batchId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (!batch.Entries.Any()) return "Cannot post empty batch.";

            // 1. Determine the Posting Date
            // FIX: Don't use DateTime.Now. Use the date of the transactions.
            // We take the date from the first entry (since cashbook pages usually group single days)
            // Or max date to be safe.
            DateOnly postingDate = DateOnly.FromDateTime(batch.Entries.First().TransactionDate);

            // 2. Prepare GL Lines
            var glLines = new List<GLJournalLine>();

            // A. The Offset Lines (The "Other side" of the transaction)
            foreach (var entry in batch.Entries)
            {
                glLines.Add(new GLJournalLine
                {
                    AccountId = entry.OffsetAccountId,  // If Cashbook Debit (In), Offset is Credit? No.
                                           // ACCOUNTING RULE:
                                           // If Cashbook Debit (Money In) -> Bank Debit, Offset Credit (Revenue/Income)
                                           // If Cashbook Credit (Money Out) -> Bank Credit, Offset Debit (Expense)
                                           // So we must SWAP for the offset lines.

                    // Actually, simpler logic:
                    // Cashbook Entry: Debit = 100 (Money In). 
                    // GL Impact: Bank Dr 100. Sales Cr 100.
                    // The line below represents the SALES side (Offset).
                    Debit = entry.Credit, // Swap
                    Credit = entry.Debit, // Swap

                    Reference = $"{entry.Reference}: {entry.Description}",
                });
            }

            // B. The Main Bank Line (Summary)
            // Sum of all entries to the Bank Account
            decimal totalDebit = batch.Entries.Sum(e => e.Debit);
            decimal totalCredit = batch.Entries.Sum(e => e.Credit);

            if (totalDebit > 0 || totalCredit > 0)
            {
                glLines.Add(new GLJournalLine
                {
                    AccountId = batch.BankAccountId,
                    Debit = totalDebit,
                    Credit = totalCredit,
                    Reference = $"Cashbook Batch: {batch.BatchReference}"
                });
            }

            // 3. Call GL Engine
            // We pass 'postingDate' which we extracted from the actual entries
            var (err, glBatchId) = await _glOps.CreateJournalEntryAsync(
                batch.CompanyId,
                postingDate, // <--- USING CORRECT DATE HERE
                "Cashbook Posting",
                $"Ref: {batch.BatchReference}",
                glLines
            );

            if (!string.IsNullOrEmpty(err)) return err;

            // 4. Update Cashbook Status
            batch.Status = BatchStatus.Posted;
            batch.PostedGLBatchId = glBatchId;
            await ctx.SaveChangesAsync();

            // 5. Auto-Post the GL Batch (so user doesn't have to go to GL module)
            if (glBatchId.HasValue)
            {
                await _glOps.PostBatchAsync(batch.CompanyId, glBatchId.Value);
            }

            return string.Empty;
        }
    }
}