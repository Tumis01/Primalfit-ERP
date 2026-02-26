using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class InventoryValuationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public InventoryValuationService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }


        public async Task<string> RecalculateWACC(Guid goodsReceiptId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch the GRN with its lines
            var grn = await ctx.GoodsReceipts
                .Include(g => g.Lines)
                .FirstOrDefaultAsync(g => g.Id == goodsReceiptId);

            if (grn == null) return "Goods Receipt not found.";

            // 2. Fetch the linked Purchase Order to get the base prices and Exchange Rate
            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId);

            if (po == null) return "Linked Purchase Order not found.";

            // 3. Fetch all Landed Costs added to this GRN (e.g. $100 Freight)
            var landedCosts = await ctx.GrnLandedCosts
                .Where(lc => lc.GoodsReceiptId == goodsReceiptId)
                .ToListAsync();

            decimal totalCostByValue = landedCosts.Where(x => x.AllocationMethod == AllocationMethod.ByValue).Sum(x => x.Amount);
            decimal totalCostByQty = landedCosts.Where(x => x.AllocationMethod == AllocationMethod.ByQuantity).Sum(x => x.Amount);

            // 4. Determine GRN Totals (for proportional allocation)
            decimal totalGrnPurchaseValueBase = 0;
            decimal totalGrnQuantity = 0;

            foreach (var line in grn.Lines)
            {
                var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                if (poLine != null && line.QuantityReceived > 0)
                {
                    // Convert PO Vendor Currency to Base Company Currency
                    decimal baseLineValue = (poLine.UnitCost * line.QuantityReceived) * po.ExchangeRate;

                    totalGrnPurchaseValueBase += baseLineValue;
                    totalGrnQuantity += line.QuantityReceived;
                }
            }

            // 5. Process each line to calculate the TRUE unit cost and update WACC
            foreach (var line in grn.Lines.Where(l => l.QuantityReceived > 0))
            {
                var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                if (poLine == null) continue;

                var item = await ctx.Items.FirstOrDefaultAsync(i => i.Id == poLine.ItemId);
                if (item == null || item.IsService) continue; // Services don't hold inventory value

                // --- A. CALCULATE INCOMING UNIT COST ---
                decimal linePurchaseValueBase = (poLine.UnitCost * line.QuantityReceived) * po.ExchangeRate;

                // Allocate costs to this specific line
                decimal allocatedByValue = totalGrnPurchaseValueBase > 0
                    ? totalCostByValue * (linePurchaseValueBase / totalGrnPurchaseValueBase) : 0;

                decimal allocatedByQty = totalGrnQuantity > 0
                    ? totalCostByQty * (line.QuantityReceived / totalGrnQuantity) : 0;

                decimal totalLineLandedCost = allocatedByValue + allocatedByQty;
                decimal trueTotalLineCost = linePurchaseValueBase + totalLineLandedCost;

                // This is your $31 (Purchase Cost + Extra Costs / Quantity)
                decimal incomingUnitCost = trueTotalLineCost / line.QuantityReceived;

                // --- B. CALCULATE NEW WEIGHTED AVERAGE COST (WACC) ---

                // Get the current total quantity of this item in the warehouse.
                // NOTE: Because PurchasingService already saved the physical receipt to the StockLedger,
                // the Current System Qty already includes the items we just received. 
                // We must subtract them to find out what the "Old Qty" was before the truck arrived.
                decimal currentTotalQty = await ctx.StockLedgers
                    .Where(s => s.ItemId == item.Id && s.CompanyId == item.CompanyId)
                    .SumAsync(s => s.QuantityChanged);

                decimal oldQty = currentTotalQty - line.QuantityReceived;

                // Protect against negative stock messing up the math
                if (oldQty < 0) oldQty = 0;

                decimal oldWacc = item.WeightedAverageCost;
                decimal oldTotalValue = oldQty * oldWacc;

                // The Magic WACC Formula: (Old Value + New Value) / Total Qty
                decimal newWacc = 0;
                if (currentTotalQty > 0)
                {
                    newWacc = (oldTotalValue + trueTotalLineCost) / currentTotalQty;
                }

                // --- C. UPDATE DATABASE ---

                // 1. Update the Item Master
                item.WeightedAverageCost = newWacc;

                // 2. Update the Stock Ledger record to reflect the TRUE landed cost, not just the PO price
                var ledgerEntry = await ctx.StockLedgers.FirstOrDefaultAsync(s =>
                    s.Reference == grn.GrnNumber &&
                    s.ItemId == item.Id &&
                    s.QuantityChanged == line.QuantityReceived);

                if (ledgerEntry != null)
                {
                    ledgerEntry.CostAtTime = incomingUnitCost;
                }

                // 3. Log the history for the auditor
                ctx.ItemCostHistories.Add(new ItemCostHistory
                {
                    Id = Guid.NewGuid(),
                    ItemId = item.Id,
                    DateChanged = DateTime.UtcNow,
                    OldQty = oldQty,
                    OldWacc = oldWacc,
                    NewQtyIn = line.QuantityReceived,
                    NewCostIn = incomingUnitCost, // Records the $31!
                    ResultingWacc = newWacc,
                    Reference = $"GRN: {grn.GrnNumber}"
                });
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        private async Task<decimal> GetCurrentStockQty(AppDbContext ctx, Guid itemId, Guid companyId)
        {
            return await ctx.StockLedgers
                .Where(s => s.ItemId == itemId && s.CompanyId == companyId)
                .SumAsync(s => s.QuantityChanged);
        }
    }
}