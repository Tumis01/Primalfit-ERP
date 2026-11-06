using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed record ReceiptInvoiceCandidate(Guid InvoiceId, string InvoiceNumber, Guid LineId, Guid? OrderLineId,
    Guid ItemId, string ItemName, Guid? UomId, string UomName, decimal Factor, decimal Invoiced,
    decimal Received, decimal Reserved, decimal Remaining, decimal UnitCost, decimal ExchangeRate, Guid? ClearingAccountId, Guid? DimensionSetId = null);

// Allocation is calculated in base units across both order- and invoice-sourced
// receipts. A receipt that was already invoiced is attributed to its invoice line.
public static class PurchaseInvoiceReceiptSources
{
    public static async Task<List<ReceiptInvoiceCandidate>> GetAsync(AppDbContext db, Guid companyId, Guid vendorId, Guid? excludedDraftId = null, Guid? excludedReceiptId = null)
    {
        var lines = await db.PurchaseInvoiceLines.AsNoTracking().Include(x => x.PurchaseInvoice).Include(x => x.Item)
            .Include(x => x.GoodsReceiptLine).ThenInclude(x => x!.PurchaseOrderLine)
            .Where(x => x.PurchaseInvoice != null && x.PurchaseInvoice.CompanyId == companyId && x.PurchaseInvoice.VendorId == vendorId
                && x.PurchaseInvoice.IsPosted && !x.PurchaseInvoice.IsDirectBill && x.PurchaseInvoice.GLBatchId.HasValue
                && db.GLBatches.Any(b => b.Id == x.PurchaseInvoice.GLBatchId.Value && b.CompanyId == companyId && b.Status == BatchStatus.Posted)
                && x.ItemId.HasValue && x.Item != null && x.Item.CompanyId == companyId && !x.Item.IsService && x.QuantityBilled > 0).ToListAsync();
        var rootIds = lines.Where(x => x.PurchaseOrderLineId.HasValue).Select(x => x.PurchaseOrderLineId!.Value).Distinct().ToList();
        var ids = lines.Select(x => x.Id).ToList();
        var invoiceBatchIds = lines.Select(x => x.PurchaseInvoice!.GLBatchId!.Value).Distinct().ToList();
        var ledgerRows = await db.GLTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && invoiceBatchIds.Contains(x.BatchId) && x.Debit > 0)
            .Select(x => new { x.BatchId, x.SegCoaId, x.Debit, x.Narration }).ToListAsync();
        var sourceReceiptIds = lines.Where(x => x.GoodsReceiptLine != null).Select(x => x.GoodsReceiptLine!.GoodsReceiptId).Distinct().ToList();
        var postedSourceReceiptIds = await db.GoodsReceipts.AsNoTracking().Where(x => x.CompanyId == companyId && sourceReceiptIds.Contains(x.Id)
            && x.GLBatchId.HasValue && db.GLBatches.Any(b => b.Id == x.GLBatchId.Value && b.CompanyId == companyId && b.Status == BatchStatus.Posted))
            .Select(x => x.Id).ToHashSetAsync();
        var roots = await db.PurchaseOrderLines.AsNoTracking().Where(x => rootIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var orderIds = roots.Values.Select(x => x.PurchaseOrderId).Distinct().ToList();
        var orders = await db.PurchaseOrders.AsNoTracking().Where(x => orderIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var receipts = await db.GoodsReceiptLines.AsNoTracking().Where(x => x.GoodsReceiptId != excludedReceiptId && ((x.PurchaseOrderLineId.HasValue && rootIds.Contains(x.PurchaseOrderLineId.Value))
            || (x.PurchaseInvoiceLineId.HasValue && ids.Contains(x.PurchaseInvoiceLineId.Value))))
            .Select(x => new { x.Id, x.PurchaseOrderLineId, x.PurchaseInvoiceLineId, x.QuantityReceived }).ToListAsync();
        var reservations = await db.GoodsReceiptDraftLines.AsNoTracking().Where(x => x.GoodsReceiptDraftId != excludedDraftId
            && ((x.PurchaseOrderLineId.HasValue && rootIds.Contains(x.PurchaseOrderLineId.Value))
                || (x.PurchaseInvoiceLineId.HasValue && ids.Contains(x.PurchaseInvoiceLineId.Value))))
            .Select(x => new { x.PurchaseOrderLineId, x.PurchaseInvoiceLineId, x.QuantityReceived }).ToListAsync();
        var allocations = await db.PurchaseInvoiceLines.AsNoTracking().Where(x => x.PurchaseOrderLineId.HasValue && rootIds.Contains(x.PurchaseOrderLineId.Value) && x.GoodsReceiptLineId.HasValue)
            .Select(x => new { x.Id, x.PurchaseOrderLineId, x.GoodsReceiptLineId }).ToListAsync();
        var adjustments = await InvoiceCorrectionBalances.PurchaseOrderAdjustmentsAsync(db, rootIds);
        var invoiceAdjustments = await InvoiceCorrectionBalances.PurchaseInvoiceAdjustmentsAsync(db, ids);
        var result = new List<ReceiptInvoiceCandidate>();
        foreach (var line in lines)
        {
            var reference = !string.IsNullOrWhiteSpace(line.Description) ? line.Description : $"Purchase Invoice: {line.PurchaseInvoice!.ExternalInvoiceNumber}";
            var debit = Math.Round(line.LineTotal * line.PurchaseInvoice!.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            var postedAccounts = ledgerRows.Where(x => x.BatchId == line.PurchaseInvoice.GLBatchId && x.Debit == debit && x.Narration == reference)
                .Select(x => x.SegCoaId).Distinct().ToList();
            // Ambiguous or missing historical ledger detail cannot establish which
            // account to clear. Hide those lines rather than guessing a mapping.
            if (postedAccounts.Count != 1) continue;
            decimal received, reserved, remaining;
            if (line.PurchaseOrderLineId.HasValue)
            {
                var id = line.PurchaseOrderLineId.Value;
                if (!roots.TryGetValue(id, out var root) || !orders.TryGetValue(root.PurchaseOrderId, out var order)
                    || order.CompanyId != companyId || order.VendorId != vendorId || order.CurrencyId != line.PurchaseInvoice!.CurrencyId
                    || root.ItemId != line.ItemId || line.GoodsReceiptLine?.PurchaseOrderLineId != id
                    || !postedSourceReceiptIds.Contains(line.GoodsReceiptLine.GoodsReceiptId)) continue;
                var total = receipts.Where(x => x.PurchaseOrderLineId == id).Sum(x => x.QuantityReceived);
                var otherIds = allocations.Where(x => x.PurchaseOrderLineId == id && x.Id != line.Id).Select(x => x.GoodsReceiptLineId!.Value).ToHashSet();
                received = Math.Max(0, total - receipts.Where(x => otherIds.Contains(x.Id)).Sum(x => x.QuantityReceived));
                reserved = reservations.Where(x => x.PurchaseOrderLineId == id).Sum(x => x.QuantityReceived);
                remaining = Math.Min(Math.Max(0, line.QuantityBilled - received - reserved - invoiceAdjustments.GetValueOrDefault(line.Id)), Math.Max(0, root.QuantityOrdered - total - reserved - adjustments.GetValueOrDefault(id)));
            }
            else
            {
                if (line.GoodsReceiptLineId.HasValue) continue;
                received = receipts.Where(x => x.PurchaseInvoiceLineId == line.Id).Sum(x => x.QuantityReceived);
                reserved = reservations.Where(x => x.PurchaseInvoiceLineId == line.Id).Sum(x => x.QuantityReceived);
                remaining = Math.Max(0, line.QuantityBilled - received - reserved - invoiceAdjustments.GetValueOrDefault(line.Id));
            }
            if (remaining > UomConversion.QuantityTolerance) result.Add(new(line.PurchaseInvoice!.Id, line.PurchaseInvoice.PurchaseInvoiceNumber, line.Id, line.PurchaseOrderLineId,
                line.ItemId!.Value, line.Item!.Name, line.UomId, line.UomName, line.UomConversionFactor, line.QuantityBilled, received, reserved, remaining,
                line.UnitCostBilled, line.PurchaseInvoice.ExchangeRate, postedAccounts[0], line.DimensionSetId));
        }
        return result.OrderBy(x => x.InvoiceNumber).ThenBy(x => x.ItemName).ToList();
    }
}
