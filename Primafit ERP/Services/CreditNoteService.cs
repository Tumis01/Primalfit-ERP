using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class CreditNoteService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly InventoryService _invService;
        private readonly TransactionMappingService _mappingService; // <-- NEW: Injected Mapping Service

        public CreditNoteService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps, InventoryService invService, TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService; // <-- NEW
        }

        // 1. CREATE DRAFT FROM SALES ORDER
        public async Task<CreditNote> CreateDraftFromOrderAsync(Guid orderId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var so = await ctx.SalesOrders
                .Include(s => s.Lines)
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == orderId);

            if (so == null) throw new Exception("Sales Order not found.");
            if (so.Status != OrderStatus.Invoiced) throw new Exception("Only Invoiced orders can be credited.");

            // CALCULATE PREVIOUS RETURNS: Find all quantities already returned for this specific Sales Order
            var previousReturns = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status != CreditNoteStatus.Void)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .Select(g => new { SalesOrderLineId = g.Key, TotalReturned = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.SalesOrderLineId, x => x.TotalReturned);

            var creditNote = new CreditNote
            {
                Id = Guid.NewGuid(),
                CompanyId = so.CompanyId,
                SalesOrderId = so.Id,
                CustomerId = so.CustomerId,
                CurrencyId = so.CurrencyId,
                ExchangeRate = so.ExchangeRate,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = CreditNoteStatus.Draft,
                Reason = "Return / Reversal",
                ReturnToStock = false,
                WarehouseId = so.WarehouseId,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                CreditNoteNumber = $"CN-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}"
            };

            foreach (var soLine in so.Lines)
            {
                decimal alreadyReturned = previousReturns.ContainsKey(soLine.Id) ? previousReturns[soLine.Id] : 0;
                decimal maxReturnable = soLine.Quantity - alreadyReturned;

                // Only add items to the Credit Note if there is still something left to return
                if (maxReturnable > 0)
                {
                    creditNote.Lines.Add(new CreditNoteLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = creditNote.Id,
                        ItemId = soLine.ItemId ?? Guid.Empty,
                        SalesOrderLineId = soLine.Id,
                        Quantity = 0,
                        UnitPrice = soLine.UnitPrice,
                        OriginalSoldQty = soLine.Quantity,
                        MaxReturnableQty = maxReturnable
                    });
                }
            }

            if (!creditNote.Lines.Any()) throw new Exception("All items from this invoice have already been fully returned/credited.");

            creditNote.TotalAmount = creditNote.Lines.Sum(l => l.LineTotal);

            ctx.CreditNotes.Add(creditNote);
            await ctx.SaveChangesAsync();

            return creditNote;
        }

        public async Task<CreditNote?> GetByIdAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.CreditNotes
                .Include(c => c.Lines).ThenInclude(l => l.Item)
                .Include(c => c.Customer)
                .Include(c => c.SalesOrder)
                .Include(c => c.Currency)
                .Include(c => c.Warehouse)
                // Guard against URL record tampering across distinct corporate contexts
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);
        }

        // 3. SAVE DRAFT 
        public async Task<string> SaveDraftAsync(CreditNote note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.CreditNotes.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == note.Id);

            if (existing == null) return "Credit Note not found.";
            if (existing.Status == CreditNoteStatus.Posted) return "Cannot edit a posted Credit Note.";

            existing.Date = note.Date;
            existing.Reason = note.Reason;
            existing.ReturnToStock = note.ReturnToStock;
            existing.WarehouseId = note.WarehouseId;

            ctx.CreditNoteLines.RemoveRange(existing.Lines);
            foreach (var line in note.Lines)
            {
                if (line.Quantity < 0) return $"Item {line.ItemId}: Quantity cannot be negative.";
                ctx.CreditNoteLines.Add(new CreditNoteLine
                {
                    Id = Guid.NewGuid(),
                    HeaderId = existing.Id,
                    ItemId = line.ItemId,
                    SalesOrderLineId = line.SalesOrderLineId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                });
            }
            existing.TotalAmount = note.Lines.Sum(l => l.LineTotal);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 4. POST CREDIT NOTE
        public async Task<string> PostCreditNoteAsync(Guid cnId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                var cn = await ctx.CreditNotes
                    .Include(c => c.Lines).ThenInclude(l => l.Item)
                    .Include(c => c.Customer)
                    .Include(c => c.SalesOrder)
                    .FirstOrDefaultAsync(c => c.Id == cnId);

                if (cn == null) return "Credit Note not found.";
                if (cn.Status == CreditNoteStatus.Posted) return "Already posted.";

                var glLines = new List<GLJournalLine>();
                decimal totalCreditsBase = 0;

                // 1. Debit Revenue
                foreach (var line in cn.Lines)
                {
                    if (line.Quantity == 0) continue;

                    if (line.Item.SalesIncomeAccountId == Guid.Empty)
                        return $"Item '{line.Item.Name}' missing Sales Income GL Account.";

                    // --- THE FIX: INTERCEPT REVENUE ACCOUNT FOR REVERSAL ---
                    // Reversing Sales Revenue is a Debit
                    Guid revenueAccount = await _mappingService.GetMappedAccountAsync(
                        cn.CompanyId,
                        SystemTransactionType.CreditNote,
                        isDebit: true,
                        defaultAccountId: line.Item.SalesIncomeAccountId);

                    decimal lineTotalBase = Math.Round(line.LineTotal * cn.ExchangeRate, 2);

                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = revenueAccount,
                        Debit = lineTotalBase,
                        Credit = 0,
                        Reference = $"CN Reversal: {line.Item.Name}"
                    });

                    totalCreditsBase += lineTotalBase;
                }

                // 2. Credit Accounts Receivable
                if (cn.Customer.ReceivablesAccountId == null) return "Customer AR Account is missing.";

                // --- THE FIX: INTERCEPT AR ACCOUNT FOR REVERSAL ---
                // Reversing AR is a Credit
                Guid arAccount = await _mappingService.GetMappedAccountAsync(
                    cn.CompanyId,
                    SystemTransactionType.CreditNote,
                    isDebit: false,
                    defaultAccountId: cn.Customer.ReceivablesAccountId.Value);

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = arAccount,
                    Debit = 0,
                    Credit = totalCreditsBase,
                    Reference = $"CN {cn.CreditNoteNumber} for {cn.Customer.Name}"
                });

                var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(
                    cn.CompanyId, cn.Date, "Credit Note", $"CN {cn.CreditNoteNumber}", glLines, userId.ToString());

                if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                if (batchId.HasValue) await _glOps.PostBatchAsync(cn.CompanyId, batchId.Value, userId.ToString());
                cn.GlBatchId = batchId;

                // --- B. INVENTORY RETURN (Unchanged Logic) ---
                if (cn.ReturnToStock)
                {
                    if (cn.WarehouseId == null || cn.WarehouseId == Guid.Empty)
                        throw new Exception("Warehouse is required when 'Return to Stock' is enabled.");

                    var cogsGlLines = new List<GLJournalLine>();

                    foreach (var line in cn.Lines)
                    {
                        if (line.Item.IsService) continue;

                        ctx.StockLedgers.Add(new StockLedger
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = cn.CompanyId,
                            ItemId = line.ItemId,
                            WarehouseId = cn.WarehouseId.Value,
                            QuantityChanged = line.Quantity,
                            Type = StockMovementType.SalesReturn,
                            CostAtTime = line.Item.WeightedAverageCost,
                            Reference = cn.CreditNoteNumber,
                            Date = DateTime.UtcNow
                        });

                        decimal cogsValue = line.Quantity * line.Item.WeightedAverageCost;
                        if (cogsValue > 0)
                        {
                            cogsGlLines.Add(new GLJournalLine { SegCoaId = line.Item.InventoryAssetAccountId, Debit = cogsValue, Credit = 0, Reference = $"Stock Return: {line.Item.Name}" });
                            cogsGlLines.Add(new GLJournalLine { SegCoaId = line.Item.CostOfGoodsSoldAccountId, Debit = 0, Credit = cogsValue, Reference = $"COGS Reversal: {line.Item.Name}" });
                        }
                    }

                    if (cogsGlLines.Any())
                    {
                        var (cogsErr, cogsBatchId) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "CN Inventory", $"Stock Return {cn.CreditNoteNumber}", cogsGlLines, userId.ToString());
                        if (!string.IsNullOrEmpty(cogsErr)) throw new Exception(cogsErr);
                        if (cogsBatchId.HasValue) await _glOps.PostBatchAsync(cn.CompanyId, cogsBatchId.Value, userId.ToString());
                    }
                }

                cn.Status = CreditNoteStatus.Posted;
                cn.PostedAt = DateTime.UtcNow;
                cn.PostedByUserId = userId;

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Error posting Credit Note: {ex.Message}";
            }
        }

        public async Task<string> DeleteDraftAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var cn = await ctx.CreditNotes.FindAsync(id);
            if (cn == null || cn.Status != CreditNoteStatus.Draft) return "Cannot delete.";
            ctx.CreditNotes.Remove(cn);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}