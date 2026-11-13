using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services;

public sealed record ReceiptQuantityUpdate(Guid LineId, decimal QuantityBase);

public partial class PurchasingService
{
    public Task<string> UpdateRejectedGoodsReceiptAsync(GoodsReceipt receipt, Guid warehouseId, string userId)
    {
        receipt.WarehouseId = warehouseId;
        return SaveGoodsReceiptAsync(receipt, warehouseId, userId, updatingReview: true, fullCorrection: true);
    }

    public Task<string> UpdateGoodsReceiptQuantitiesAsync(Guid companyId, Guid receiptId,
        IReadOnlyCollection<ReceiptQuantityUpdate> quantities, string userId) => SaveGoodsReceiptAsync(
            new GoodsReceipt { Id = receiptId, CompanyId = companyId,
                Lines = quantities.Select(x => new GoodsReceiptLine { Id = x.LineId, QuantityReceived = x.QuantityBase }).ToList() },
            Guid.Empty, userId, updatingReview: true);

    private static async Task<(string Error, GoodsReceipt? Receipt, Dictionary<Guid, decimal>? OriginalQuantities)> PrepareReceiptReviewUpdateAsync(
        AppDbContext db, GoodsReceipt input, bool fullCorrection)
    {
        var receipt = await db.GoodsReceipts
            .FromSqlInterpolated($"SELECT * FROM [GoodsReceipts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {input.Id} AND [CompanyId] = {input.CompanyId}")
            .Include(x => x.Lines).FirstOrDefaultAsync();
        if (receipt == null) return ("Receipt not found.", null, null);
        if (!receipt.GLBatchId.HasValue) return ("This receipt has no review batch and cannot be edited.", null, null);
        var batch = await db.GLBatches
            .FromSqlInterpolated($"SELECT * FROM [GLBatches] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {receipt.GLBatchId.Value} AND [CompanyId] = {input.CompanyId}")
            .FirstOrDefaultAsync();
        if (batch == null || batch.Status == BatchStatus.Posted || await db.GLTransactions.AnyAsync(x => x.BatchId == batch.Id))
            return ("Posted receipts cannot be edited. Refresh the receipt register.", null, null);
        if (fullCorrection && batch.Status != BatchStatus.Rejected)
            return ("Full receipt corrections require a rejected review batch. Refresh the receipt register.", null, null);
        var ids = receipt.Lines.Select(x => x.Id).ToHashSet();
        if (input.Lines.Count != ids.Count || input.Lines.Select(x => x.Id).Distinct().Count() != ids.Count
            || input.Lines.Any(x => !ids.Contains(x.Id) || x.QuantityReceived <= 0))
            return ("Enter a positive quantity for every existing receipt line. Receipt sources cannot be added or removed during GL review.", null, null);
        if (await db.PurchaseInvoiceLines.AnyAsync(x => x.GoodsReceiptLineId.HasValue && ids.Contains(x.GoodsReceiptLineId.Value)))
            return ("This receipt is already used by a purchase invoice and its quantities cannot be edited.", null, null);
        var original = receipt.Lines.ToDictionary(x => x.Id, x => x.QuantityReceived);
        var incoming = input.Lines.ToDictionary(x => x.Id);
        if (fullCorrection)
        {
            if (input.DateReceived == default || !input.WarehouseId.HasValue)
                return ("Select a receiving warehouse and receipt date.", null, null);
            receipt.WarehouseId = input.WarehouseId;
            receipt.DateReceived = input.DateReceived;
            receipt.CustomTransactionTypeId = input.CustomTransactionTypeId;
            receipt.OverrideInventoryAssetGlAccountId = input.OverrideInventoryAssetGlAccountId;
            receipt.OverrideGrIrClearingGlAccountId = input.OverrideGrIrClearingGlAccountId;
            receipt.InventoryGlAccountId = input.InventoryGlAccountId;
        }
        foreach (var line in receipt.Lines)
        {
            var update = incoming[line.Id];
            line.QuantityReceived = update.QuantityReceived;
            if (fullCorrection)
            {
                if (update.PurchaseOrderLineId != line.PurchaseOrderLineId || update.PurchaseInvoiceLineId != line.PurchaseInvoiceLineId)
                    return ("A corrected receipt line must retain its source document.", null, null);
                line.UomId = update.UomId;
                line.UomName = update.UomName;
                line.UomConversionFactor = update.UomConversionFactor;
                line.DimensionSetId = update.DimensionSetId;
            }
        }
        return (string.Empty, receipt, original);
    }

    private static async Task<string> UpdateReceiptStockAsync(AppDbContext db, GoodsReceipt receipt,
        List<StockLedger> replacements, Dictionary<Guid, decimal> originalValues, string userId)
    {
        // Review movements are intentionally hidden from normal availability queries.
        // Edit the staged rows themselves, including the receipt's existing rows.
        var previous = await db.StockLedgers.IgnoreQueryFilters().Where(x => x.CompanyId == receipt.CompanyId
            && x.Reference == receipt.GrnNumber && x.Type == StockMovementType.Purchase).ToListAsync();
        var originalWarehouse = db.Entry(receipt).Property(x => x.WarehouseId).OriginalValue;
        if (previous.Any(x => x.GLBatchId != receipt.GLBatchId || x.WarehouseId != originalWarehouse))
            return "The receipt's stock movements do not match its review batch. Reload the receipt before editing.";
        var itemIds = previous.Select(x => x.ItemId).Concat(replacements.Select(x => x.ItemId)).Distinct().OrderBy(x => x).ToList();
        foreach (var itemId in itemIds)
        {
            var item = await db.Items.FromSqlInterpolated($"SELECT * FROM [Items] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {itemId} AND [CompanyId] = {receipt.CompanyId}").FirstAsync();
            var oldQty = previous.Where(x => x.ItemId == itemId).Sum(x => x.QuantityChanged);
            var newQty = replacements.Where(x => x.ItemId == itemId).Sum(x => x.QuantityChanged);
            var deltaQty = newQty - oldQty;
            var deltaValue = replacements.Where(x => x.ItemId == itemId).Sum(x => x.QuantityChanged * x.CostAtTime) - originalValues.GetValueOrDefault(itemId);
            var currentQty = await db.StockLedgers.IgnoreQueryFilters().Where(x => x.CompanyId == receipt.CompanyId && x.ItemId == itemId).SumAsync(x => x.QuantityChanged);
            if (currentQty + deltaQty < -UomConversion.QuantityTolerance)
                return $"The received stock for '{item.Name}' has already been used. The revised quantity would make stock negative.";
            foreach (var warehouseId in previous.Where(x => x.ItemId == itemId).Select(x => x.WarehouseId)
                .Concat(replacements.Where(x => x.ItemId == itemId).Select(x => x.WarehouseId)).Distinct())
            {
                var warehouseQty = await db.StockLedgers.IgnoreQueryFilters().Where(x => x.CompanyId == receipt.CompanyId && x.ItemId == itemId && x.WarehouseId == warehouseId).SumAsync(x => x.QuantityChanged);
                var change = replacements.Where(x => x.ItemId == itemId && x.WarehouseId == warehouseId).Sum(x => x.QuantityChanged)
                    - previous.Where(x => x.ItemId == itemId && x.WarehouseId == warehouseId).Sum(x => x.QuantityChanged);
                if (warehouseQty + change < -UomConversion.QuantityTolerance)
                    return $"The received stock for '{item.Name}' has already been used in this warehouse. The correction would make stock negative.";
            }
            if (deltaQty == 0 && Math.Abs(deltaValue) < 0.00000001m) continue;
            var oldWacc = item.WeightedAverageCost;
            var resultingQty = currentQty + deltaQty;
            var resultingValue = currentQty * oldWacc + deltaValue;
            if (resultingQty > UomConversion.QuantityTolerance && resultingValue < -0.01m)
                return $"The received value for '{item.Name}' has already been used. The revised quantity would make inventory value negative.";
            item.WeightedAverageCost = resultingQty > UomConversion.QuantityTolerance
                ? Math.Round(Math.Max(0, resultingValue) / resultingQty, 4, MidpointRounding.AwayFromZero) : 0;
            // A correction at edit time preserves the original cost trail and avoids
            // counting the entire receipt a second time in weighted average cost.
            db.ItemCostHistories.Add(new ItemCostHistory { ItemId = itemId, OldQty = currentQty, OldWacc = oldWacc,
                NewQtyIn = deltaQty, NewCostIn = deltaQty == 0 ? deltaValue : deltaValue / deltaQty,
                ResultingWacc = item.WeightedAverageCost, Reference = $"Receipt quantity update: {receipt.GrnNumber}", DateChanged = DateTime.UtcNow });
        }
        db.StockLedgers.RemoveRange(previous);
        db.AuditLogs.Add(new AuditLog { CompanyId = receipt.CompanyId, UserId = userId, Action = "GoodsReceiptUpdated",
            EntityType = nameof(GoodsReceipt), EntityId = receipt.Id,
            Details = $"Receipt {receipt.GrnNumber} corrected during GL review; warehouse {originalWarehouse} -> {receipt.WarehouseId}; stock and the existing review batch were updated together." });
        return string.Empty;
    }
}
