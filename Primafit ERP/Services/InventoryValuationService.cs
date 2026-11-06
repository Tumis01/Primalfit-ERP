using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class InventoryValuationService
    {
        public static bool LandedCostsEnabled => false;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public InventoryValuationService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<string> RecalculateWACC(Guid goodsReceiptId)
        {
            if (!LandedCostsEnabled) return "Landed costs are currently disabled.";
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var grn = await ctx.GoodsReceipts
                .Include(g => g.Lines)
                .FirstOrDefaultAsync(g => g.Id == goodsReceiptId);
            if (grn == null) return "Goods Receipt not found.";

            if (grn.Lines.Any(line => !line.PurchaseOrderLineId.HasValue && !line.PurchaseInvoiceLineId.HasValue))
                return "This legacy goods receipt has an unresolved purchase order link. Restore its source before recalculating valuation.";
            var poLineIds = grn.Lines.Where(line => line.PurchaseOrderLineId.HasValue).Select(line => line.PurchaseOrderLineId!.Value).Distinct().ToList();
            var poLines = await ctx.PurchaseOrderLines.Where(line => poLineIds.Contains(line.Id)).ToDictionaryAsync(line => line.Id);
            if (poLines.Count != poLineIds.Count) return "One or more linked purchase order lines were not found.";
            var poIds = poLines.Values.Select(line => line.PurchaseOrderId).Distinct().ToList();
            var purchaseOrders = await ctx.PurchaseOrders.Where(order => poIds.Contains(order.Id) && order.CompanyId == grn.CompanyId)
                .ToDictionaryAsync(order => order.Id);
            if (purchaseOrders.Count != poIds.Count) return "One or more linked purchase orders were not found.";
            var invoiceLineIds = grn.Lines.Where(x => x.PurchaseInvoiceLineId.HasValue).Select(x => x.PurchaseInvoiceLineId!.Value).Distinct().ToList();
            var invoiceLines = await ctx.PurchaseInvoiceLines.Include(x => x.PurchaseInvoice).Where(x => invoiceLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (invoiceLines.Count != invoiceLineIds.Count || invoiceLines.Values.Any(x => x.PurchaseInvoice == null || x.PurchaseInvoice.CompanyId != grn.CompanyId || !x.ItemId.HasValue)) return "One or more linked invoice lines were not found.";

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
                    PoLine = line.PurchaseOrderLineId.HasValue ? poLines.GetValueOrDefault(line.PurchaseOrderLineId.Value) : null,
                    InvoiceLine = line.PurchaseInvoiceLineId.HasValue ? invoiceLines.GetValueOrDefault(line.PurchaseInvoiceLineId.Value) : null,
                    Order = line.PurchaseOrderLineId.HasValue ? purchaseOrders.GetValueOrDefault(poLines.GetValueOrDefault(line.PurchaseOrderLineId.Value)?.PurchaseOrderId ?? Guid.Empty) : null
                })
                .Where(x => x.InvoiceLine != null || (x.PoLine != null && x.Order != null))
                .ToList();

            decimal totalGrnPurchaseValueBase = receiptLines.Sum(x =>
                (x.InvoiceLine?.UnitCostBilled ?? x.PoLine!.UnitCost) * x.Line.QuantityReceived * (x.InvoiceLine?.PurchaseInvoice?.ExchangeRate ?? x.Order!.ExchangeRate));
            decimal totalGrnQuantity = receiptLines.Sum(x => x.Line.QuantityReceived);

            // The GRN posting already writes quantity, WACC, and one audit record.
            // Finalising landed cost must amend that receipt, never post it again.
            var affectedItemIds = new HashSet<Guid>();
            var usedHistoryIds = new HashSet<Guid>();
            var usedLedgerIds = new HashSet<Guid>();

            foreach (var receiptLine in receiptLines)
            {
                var itemId = receiptLine.InvoiceLine?.ItemId ?? receiptLine.PoLine!.ItemId;
                var item = await ctx.Items.FirstOrDefaultAsync(i => i.Id == itemId);
                if (item == null || item.IsService) continue;

                decimal purchaseValueBase = (receiptLine.InvoiceLine?.UnitCostBilled ?? receiptLine.PoLine!.UnitCost) * receiptLine.Line.QuantityReceived * (receiptLine.InvoiceLine?.PurchaseInvoice?.ExchangeRate ?? receiptLine.Order!.ExchangeRate);
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
                    .FirstOrDefaultAsync(s => !usedLedgerIds.Contains(s.Id));
                if (ledgerEntry != null)
                {
                    usedLedgerIds.Add(ledgerEntry.Id);
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
