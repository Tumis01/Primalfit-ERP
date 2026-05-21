using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PurchaseReturnService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly InventoryService _invService;
        private readonly TransactionMappingService _mappingService; // <-- NEW: Injected Mapping Service

        public PurchaseReturnService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            InventoryService invService,
            TransactionMappingService mappingService) // <-- NEW
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService; // <-- NEW
        }

        // 1. INITIALIZE RETURN (Wizard Step 1)
        public async Task<PurchaseReturn> CreateDraftFromBillAsync(Guid billId, Guid warehouseId, Guid userId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Enforce company isolation by including CompanyId in the predicate
            var bill = await ctx.VendorBills
                .AsNoTracking()
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == billId && b.CompanyId == companyId);

            if (bill == null) throw new Exception("Vendor Bill not found or access denied.");

            var rtv = new PurchaseReturn
            {
                Id = Guid.NewGuid(),
                CompanyId = bill.CompanyId,
                VendorId = bill.VendorId,
                VendorBillId = bill.Id,
                WarehouseId = warehouseId,
                ReturnDate = DateTime.Today,
                CurrencyId = bill.CurrencyId,
                ExchangeRate = bill.ExchangeRate,
                Status = ReturnStatus.Draft,
                CreatedByUserId = userId,
                ReturnNumber = $"RTV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}"
            };

            var itemIds = bill.Lines.Select(l => l.ItemId).Distinct().ToList();
            var itemsMap = await ctx.Items
                .AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            var postedReturnLines = await ctx.PurchaseReturnLines
                .AsNoTracking()
                .Include(l => l.VendorBillLine)
                .Where(r => r.VendorBillLine.VendorBillId == billId)
                .ToListAsync();

            foreach (var billLine in bill.Lines)
            {
                if (!itemsMap.TryGetValue(billLine.ItemId ?? Guid.Empty, out var item) || item.IsService)
                    continue;

                decimal alreadyReturned = postedReturnLines
                    .Where(r => r.VendorBillLineId == billLine.Id)
                    .Sum(r => r.QtyReturning);

                decimal maxQty = billLine.QuantityBilled - alreadyReturned;

                if (maxQty > 0)
                {
                    rtv.Lines.Add(new PurchaseReturnLine
                    {
                        Id = Guid.NewGuid(),
                        PurchaseReturnId = rtv.Id,
                        VendorBillLineId = billLine.Id,
                        ItemId = billLine.ItemId ?? Guid.Empty,
                        ItemName = item.Name,
                        UnitCost = billLine.UnitCostBilled,
                        QtyReturning = 0
                    });
                }
            }

            return rtv;
        }

        public async Task<string> TerminateReturnAsync(Guid rtvId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var rtv = await ctx.PurchaseReturns
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == rtvId);

            if (rtv == null) return "Return not found.";
            if (rtv.Status == ReturnStatus.Posted) return "Cannot terminate a return that has already been posted.";

            try
            {
                ctx.PurchaseReturnLines.RemoveRange(rtv.Lines);
                ctx.PurchaseReturns.Remove(rtv);

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Termination Error: {ex.Message}";
            }
        }

        public async Task<string> SaveDraftAsync(PurchaseReturn rtv)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            try
            {
                rtv.TotalAmount = rtv.Lines.Sum(l => l.LineTotal);

                var validationErr = await ValidateReturnQuantities(ctx, rtv);
                if (!string.IsNullOrEmpty(validationErr)) return validationErr;

                var existing = await ctx.PurchaseReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == rtv.Id);

                if (existing == null)
                {
                    rtv.VendorBill = null;
                    foreach (var l in rtv.Lines)
                    {
                        if (l.Id == Guid.Empty) l.Id = Guid.NewGuid();
                        l.VendorBillLine = null;
                        l.PurchaseReturn = null;
                        l.PurchaseReturnId = rtv.Id;
                    }
                    ctx.PurchaseReturns.Add(rtv);
                }
                else
                {
                    if (existing.Status == ReturnStatus.Posted) return "Cannot edit a posted return.";

                    ctx.Entry(existing).CurrentValues.SetValues(rtv);

                    ctx.PurchaseReturnLines.RemoveRange(existing.Lines);
                    foreach (var l in rtv.Lines)
                    {
                        var newLine = new PurchaseReturnLine
                        {
                            Id = Guid.NewGuid(),
                            PurchaseReturnId = existing.Id,
                            VendorBillLineId = l.VendorBillLineId,
                            ItemId = l.ItemId,
                            ItemName = l.ItemName,
                            QtyReturning = l.QtyReturning,
                            UnitCost = l.UnitCost
                        };
                        ctx.PurchaseReturnLines.Add(newLine);
                    }
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Save Error: {ex.Message} {(ex.InnerException != null ? " -> " + ex.InnerException.Message : "")}";
            }
        }

        // 3. POST RETURN (Atomic Transaction)
        public async Task<string> PostReturnAsync(Guid rtvId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                var rtv = await ctx.PurchaseReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == rtvId);

                if (rtv == null) return "Return not found.";
                if (rtv.Status == ReturnStatus.Posted) return "Already posted.";
                if (rtv.Lines.Sum(l => l.QtyReturning) <= 0) return "Nothing to return (Qty is 0).";

                var valErr = await ValidateReturnQuantities(ctx, rtv);
                if (!string.IsNullOrEmpty(valErr)) return valErr;

                var originalBill = await ctx.VendorBills.FindAsync(rtv.VendorBillId);
                if (originalBill == null) return "Original Bill not found.";

                // --- A. STOCK LEDGER (Physical Reversal) ---
                foreach (var line in rtv.Lines.Where(l => l.QtyReturning > 0))
                {
                    decimal currentStock = await ctx.StockLedgers
                        .Where(s => s.ItemId == line.ItemId && s.WarehouseId == rtv.WarehouseId)
                        .SumAsync(s => s.QuantityChanged);

                    if (currentStock < line.QtyReturning)
                        throw new Exception($"Insufficient stock for {line.ItemName}. Have {currentStock.ToString("N2")} in this warehouse, trying to return {line.QtyReturning.ToString("N2")}.");

                    ctx.StockLedgers.Add(new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = rtv.CompanyId,
                        ItemId = line.ItemId,
                        WarehouseId = rtv.WarehouseId,
                        QuantityChanged = -line.QtyReturning, // NEGATIVE
                        Type = StockMovementType.PurchaseReturn,
                        CostAtTime = line.UnitCost,
                        Reference = rtv.ReturnNumber,
                        Date = DateTime.UtcNow
                    });
                }

                // --- B. FINANCIALS (GL) ---
                var glLines = new List<GLJournalLine>();

                decimal totalReturnForeign = rtv.Lines.Sum(l => l.QtyReturning * l.UnitCost);
                decimal totalReturnBase = Math.Round(totalReturnForeign * rtv.ExchangeRate, 2);

                // 1. DEBIT ACCOUNTS PAYABLE (We owe less)
                // --- THE FIX: INTERCEPT AP ACCOUNT FOR REVERSAL ---
                Guid apAccount = await _mappingService.GetMappedAccountAsync(
                    rtv.CompanyId,
                    SystemTransactionType.ReturnToVendor,
                    isDebit: true,
                    defaultAccountId: originalBill.AccountsPayableGlId);

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = apAccount,
                    Debit = totalReturnBase,
                    Credit = 0,
                    Reference = $"RTV: {rtv.ReturnNumber}"
                });

                // 2. CREDIT INVENTORY/EXPENSE (Reversing the original purchase)
                foreach (var line in rtv.Lines.Where(l => l.QtyReturning > 0))
                {
                    var originalBillLine = await ctx.VendorBillLines.FindAsync(line.VendorBillLineId);
                    if (originalBillLine == null) throw new Exception($"Original bill line missing for {line.ItemName}");

                    decimal lineTotalBase = Math.Round((line.QtyReturning * line.UnitCost) * rtv.ExchangeRate, 2);

                    // --- THE FIX: INTERCEPT EXPENSE ACCOUNT FOR REVERSAL ---
                    Guid expenseAccount = await _mappingService.GetMappedAccountAsync(
                        rtv.CompanyId,
                        SystemTransactionType.ReturnToVendor,
                        isDebit: false, // Returning an expense is a Credit
                        defaultAccountId: originalBillLine.ExpenseGlAccountId);

                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = expenseAccount,
                        Debit = 0,
                        Credit = lineTotalBase,
                        Reference = $"Return: {line.ItemName} x{line.QtyReturning}"
                    });
                }

                var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(
                    rtv.CompanyId,
                    DateOnly.FromDateTime(rtv.ReturnDate),
                    "Purchase Return",
                    rtv.ReturnNumber,
                    glLines,
                    userId.ToString());

                if (!string.IsNullOrEmpty(glErr)) throw new Exception($"GL Error: {glErr}");

                if (batchId.HasValue) await _glOps.PostBatchAsync(rtv.CompanyId, batchId.Value, userId.ToString());

                // --- C. FINALIZE ---
                rtv.Status = ReturnStatus.Posted;
                rtv.PostedAt = DateTime.UtcNow;
                rtv.PostedByUserId = userId;

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Post Error: {ex.Message}";
            }
        }

        private async Task<string> ValidateReturnQuantities(AppDbContext ctx, PurchaseReturn rtv)
        {
            var returnHistory = await ctx.PurchaseReturnLines
                .AsNoTracking()
                .Include(l => l.PurchaseReturn)
                .Where(l => l.VendorBillLine.VendorBillId == rtv.VendorBillId
                         && l.PurchaseReturn.Status == ReturnStatus.Posted)
                .ToListAsync();

            var billLines = await ctx.VendorBillLines
                .AsNoTracking()
                .Where(l => l.VendorBillId == rtv.VendorBillId)
                .ToListAsync();

            foreach (var line in rtv.Lines)
            {
                var originalLine = billLines.FirstOrDefault(b => b.Id == line.VendorBillLineId);
                if (originalLine == null) return $"Line for item {line.ItemName} not found on original bill.";

                decimal previouslyReturned = returnHistory
                    .Where(h => h.VendorBillLineId == line.VendorBillLineId)
                    .Sum(h => h.QtyReturning);

                if ((line.QtyReturning + previouslyReturned) > originalLine.QuantityBilled)
                {
                    return $"Over-return for {line.ItemName}. Max: {originalLine.QuantityBilled - previouslyReturned}, Tried: {line.QtyReturning}";
                }
            }
            return string.Empty;
        }
    }
}