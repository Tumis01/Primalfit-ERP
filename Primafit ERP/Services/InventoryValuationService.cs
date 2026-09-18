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

            var grn = await ctx.GoodsReceipts
                .Include(g => g.Lines)
                .FirstOrDefaultAsync(g => g.Id == goodsReceiptId);
            if (grn == null) return "Goods Receipt not found.";

            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId);
            if (po == null) return "Linked Purchase Order not found.";

            var landedCosts = await ctx.GrnLandedCosts
                .Where(lc => lc.GoodsReceiptId == goodsReceiptId)
                .ToListAsync();

            decimal totalCostByValue = landedCosts
                .Where(x => x.AllocationMethod == AllocationMethod.ByValue)
                .Sum(x => x.Amount);
            decimal totalCostByQty = landedCosts
                .Where(x => x.AllocationMethod == AllocationMethod.ByQuantity)
                .Sum(x => x.Amount);

            var receiptLines = grn.Lines
                .Where(line => line.QuantityReceived > 0)
                .Select(line => new
                {
                    Line = line,
                    PoLine = po.Lines.FirstOrDefault(poLine => poLine.Id == line.PurchaseOrderLineId)
                })
                .Where(x => x.PoLine != null)
                .ToList();

            decimal totalGrnPurchaseValueBase = receiptLines.Sum(x =>
                x.PoLine!.UnitCost * x.Line.QuantityReceived * po.ExchangeRate);
            decimal totalGrnQuantity = receiptLines.Sum(x => x.Line.QuantityReceived);

            // The GRN posting already writes quantity, WACC, and one audit record.
            // Finalising landed cost must amend that receipt, never post it again.
            var affectedItemIds = new HashSet<Guid>();
            var usedHistoryIds = new HashSet<Guid>();

            foreach (var receiptLine in receiptLines)
            {
                var poLine = receiptLine.PoLine!;
                var item = await ctx.Items.FirstOrDefaultAsync(i => i.Id == poLine.ItemId);
                if (item == null || item.IsService) continue;

                decimal purchaseValueBase = poLine.UnitCost * receiptLine.Line.QuantityReceived * po.ExchangeRate;
                decimal allocatedByValue = totalGrnPurchaseValueBase > 0
                    ? totalCostByValue * purchaseValueBase / totalGrnPurchaseValueBase
                    : 0m;
                decimal allocatedByQty = totalGrnQuantity > 0
                    ? totalCostByQty * receiptLine.Line.QuantityReceived / totalGrnQuantity
                    : 0m;
                decimal incomingUnitCost = (purchaseValueBase + allocatedByValue + allocatedByQty)
                    / receiptLine.Line.QuantityReceived;

                var originalHistory = await ctx.ItemCostHistories
                    .Where(h => h.ItemId == item.Id
                        && h.Reference == grn.GrnNumber
                        && h.NewQtyIn == receiptLine.Line.QuantityReceived)
                    .OrderBy(h => h.DateChanged)
                    .FirstOrDefaultAsync(h => !usedHistoryIds.Contains(h.Id));

                if (originalHistory == null)
                {
                    return $"Unable to locate the original valuation entry for GRN '{grn.GrnNumber}' and item '{item.Name}'.";
                }

                usedHistoryIds.Add(originalHistory.Id);
                originalHistory.NewCostIn = Math.Round(incomingUnitCost, 4);

                var ledgerEntry = await ctx.StockLedgers
                    .Where(s => s.Reference == grn.GrnNumber
                        && s.ItemId == item.Id
                        && s.QuantityChanged == receiptLine.Line.QuantityReceived)
                    .OrderBy(s => s.Date)
                    .FirstOrDefaultAsync();
                if (ledgerEntry != null)
                {
                    ledgerEntry.CostAtTime = incomingUnitCost;
                }

                affectedItemIds.Add(item.Id);
            }

            // Versions prior to this fix recorded "GRN: ..." as a second WACC
            // movement. Fold any such legacy row back into its original receipt
            // before removing it, so prior landed costs are not lost during repair.
            var legacyDuplicateRows = await ctx.ItemCostHistories
                .Where(h => affectedItemIds.Contains(h.ItemId)
                    && h.Reference.StartsWith("GRN: "))
                .OrderBy(h => h.DateChanged)
                .ToListAsync();

            foreach (var duplicateRow in legacyDuplicateRows)
            {
                string originalReference = duplicateRow.Reference["GRN: ".Length..];
                var originalRow = await ctx.ItemCostHistories
                    .Where(h => h.ItemId == duplicateRow.ItemId
                        && h.Reference == originalReference
                        && h.NewQtyIn == duplicateRow.NewQtyIn)
                    .OrderBy(h => h.DateChanged)
                    .FirstOrDefaultAsync();

                if (originalRow != null)
                {
                    originalRow.NewCostIn = duplicateRow.NewCostIn;
                }
            }
            ctx.ItemCostHistories.RemoveRange(legacyDuplicateRows);

            foreach (var itemId in affectedItemIds)
            {
                await RebuildItemWaccAsync(ctx, itemId);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        private static async Task RebuildItemWaccAsync(AppDbContext ctx, Guid itemId)
        {
            var item = await ctx.Items.FirstAsync(i => i.Id == itemId);
            var history = await ctx.ItemCostHistories
                .Where(h => h.ItemId == itemId && !h.Reference.StartsWith("GRN: "))
                .OrderBy(h => h.DateChanged)
                .ThenBy(h => h.Id)
                .ToListAsync();
            if (!history.Any()) return;

            decimal wacc = history[0].OldWacc;
            foreach (var entry in history)
            {
                entry.OldWacc = Math.Round(wacc, 4);
                decimal newQuantity = entry.OldQty + entry.NewQtyIn;
                decimal valueChange = entry.NewQtyIn == 0m
                    ? entry.NewCostIn
                    : entry.NewQtyIn * entry.NewCostIn;

                wacc = newQuantity > 0m
                    ? Math.Round(((entry.OldQty * entry.OldWacc) + valueChange) / newQuantity, 4)
                    : 0m;
                entry.ResultingWacc = wacc;
            }

            item.WeightedAverageCost = wacc;
        }
    }
}
