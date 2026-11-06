using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

// All quantities in this calculation are in the item base unit. Fulfilled
// quantities remain cumulative: a physical return never reopens an order.
internal sealed record InvoiceLineBalance(decimal Invoiced, decimal Fulfilled, decimal Reserved,
    decimal PriorCorrected, decimal PriorUnfulfilledCorrection, decimal OtherRootCorrection,
    decimal PriorReturned, decimal OtherRootReturned, decimal RootUnfulfilled);

internal static class InvoiceCorrectionBalances
{
    public static async Task<Dictionary<Guid, decimal>> SalesInvoiceAdjustmentsAsync(AppDbContext db, IEnumerable<Guid> lineIds)
    {
        var ids = lineIds.Distinct().ToList();
        return await db.CreditNoteLines.AsNoTracking().Where(x => x.SalesInvoiceLineId.HasValue && ids.Contains(x.SalesInvoiceLineId.Value)
            && x.Header!.ReturnToStock && x.Header.Status != CreditNoteStatus.Void && (x.Header.Status == CreditNoteStatus.Draft
                || (x.Header.Status == CreditNoteStatus.Posted && (!x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))))
            .GroupBy(x => x.SalesInvoiceLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.UnfulfilledQuantity) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
    }

    public static async Task<Dictionary<Guid, decimal>> PurchaseInvoiceAdjustmentsAsync(AppDbContext db, IEnumerable<Guid> lineIds)
    {
        var ids = lineIds.Distinct().ToList();
        return await db.DebitNoteLines.AsNoTracking().Where(x => x.PurchaseInvoiceLineId.HasValue && ids.Contains(x.PurchaseInvoiceLineId.Value)
            && x.Header!.Status != DebitNoteStatus.Void && (x.Header.Status == DebitNoteStatus.Draft
                || (x.Header.Status == DebitNoteStatus.Posted && (!x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))))
            .GroupBy(x => x.PurchaseInvoiceLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.UnfulfilledQuantity) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
    }

    public static async Task<Dictionary<Guid, decimal>> SalesOrderAdjustmentsAsync(AppDbContext db, IEnumerable<Guid> lineIds)
    {
        var ids = lineIds.Distinct().ToList();
        var rows = await db.CreditNoteLines.AsNoTracking().Where(x => x.SalesOrderLineId.HasValue && ids.Contains(x.SalesOrderLineId.Value)
            && x.Header!.ReturnToStock && x.Header.Status != CreditNoteStatus.Void
            && ((x.SalesInvoiceLineId.HasValue && x.Header.Status == CreditNoteStatus.Draft)
                || (x.Header.Status == CreditNoteStatus.Posted && (!x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))))
            .Select(x => new { Id = x.SalesOrderLineId!.Value, x.SalesInvoiceLineId, x.Quantity, x.UnfulfilledQuantity }).ToListAsync();
        return rows.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.SalesInvoiceLineId.HasValue ? x.UnfulfilledQuantity : x.Quantity));
    }

    public static async Task<Dictionary<Guid, decimal>> PurchaseOrderAdjustmentsAsync(AppDbContext db, IEnumerable<Guid> lineIds)
    {
        var ids = lineIds.Distinct().ToList();
        var rows = await db.DebitNoteLines.AsNoTracking().Where(x => x.PurchaseOrderLineId.HasValue && ids.Contains(x.PurchaseOrderLineId.Value)
            && x.Header!.Status != DebitNoteStatus.Void
            && ((x.PurchaseInvoiceLineId.HasValue && x.Header.Status == DebitNoteStatus.Draft)
                || (x.Header.Status == DebitNoteStatus.Posted && (!x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))))
            .Select(x => new { Id = x.PurchaseOrderLineId!.Value, x.PurchaseInvoiceLineId, x.Quantity, x.UnfulfilledQuantity }).ToListAsync();
        return rows.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.PurchaseInvoiceLineId.HasValue ? x.UnfulfilledQuantity : x.Quantity));
    }

    public static async Task<InvoiceLineBalance> SalesAsync(AppDbContext db, SalesInvoiceLine line, Guid? exceptCredit = null, Guid? exceptReturn = null)
    {
        var qty = line.QuantityInvoiced;
        var corrections = await db.CreditNoteLines.AsNoTracking().Where(x => x.HeaderId != exceptCredit && x.SalesInvoiceLineId == line.Id
            && x.Header!.ReturnToStock && x.Header.Status != CreditNoteStatus.Void
            && (x.Header.Status == CreditNoteStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .Select(x => new { x.Quantity, x.UnfulfilledQuantity }).ToListAsync();
        var returned = await db.ReceiptRefundLines.AsNoTracking().Where(x => x.HeaderId != exceptReturn && x.SalesInvoiceLineId == line.Id
            && x.Header!.Status != ReceiptRefundStatus.Void && x.Header.RefundType != ReceiptRefundType.PaymentOnly
            && (x.Header.Status == ReceiptRefundStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .SumAsync(x => x.Quantity);
        decimal fulfilled, reserved, rootUnfulfilled, otherCorrection = 0, otherReturned = 0;
        if (line.SalesOrderLineId.HasValue)
        {
            var root = await db.SalesOrderLines.AsNoTracking().SingleAsync(x => x.Id == line.SalesOrderLineId.Value);
            var movements = await db.SalesShipmentLines.AsNoTracking().Where(x => x.SalesOrderLineId == root.Id
                && (x.Shipment!.Status == ShipmentStatus.Shipped || x.Shipment.Status == ShipmentStatus.Pending))
                .Select(x => new { x.Id, x.QtyShipped, x.Shipment!.Status }).ToListAsync();
            var physical = Math.Max(root.QtyShipped, movements.Where(x => x.Status == ShipmentStatus.Shipped).Sum(x => x.QtyShipped));
            var allocatedIds = await db.SalesInvoiceLines.AsNoTracking().Where(x => x.SalesOrderLineId == root.Id && x.Id != line.Id && x.ShipmentLineId.HasValue)
                .Select(x => x.ShipmentLineId!.Value).ToListAsync();
            var otherAllocated = movements.Where(x => x.Status == ShipmentStatus.Shipped && allocatedIds.Contains(x.Id)).Sum(x => x.QtyShipped);
            fulfilled = Math.Min(qty, Math.Max(0, physical - otherAllocated));
            reserved = movements.Where(x => x.Status == ShipmentStatus.Pending).Sum(x => x.QtyShipped);
            var rootCredits = await db.CreditNoteLines.AsNoTracking().Where(x => x.HeaderId != exceptCredit && x.SalesOrderLineId == root.Id
                && x.Header!.ReturnToStock && x.Header.Status != CreditNoteStatus.Void
                && (x.Header.Status == CreditNoteStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
                .Select(x => new { x.Quantity, x.UnfulfilledQuantity, x.SalesInvoiceLineId }).ToListAsync();
            otherCorrection = rootCredits.Sum(x => x.SalesInvoiceLineId.HasValue ? x.UnfulfilledQuantity : x.Quantity);
            otherReturned = await db.ReceiptRefundLines.AsNoTracking().Where(x => x.HeaderId != exceptReturn && x.SalesOrderLineId == root.Id && !x.SalesInvoiceLineId.HasValue
                && x.Header!.Status != ReceiptRefundStatus.Void && x.Header.RefundType != ReceiptRefundType.PaymentOnly
                && (x.Header.Status == ReceiptRefundStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
                .SumAsync(x => x.Quantity);
            rootUnfulfilled = Math.Max(0, root.Quantity - physical - otherCorrection);
        }
        else
        {
            var movements = await db.SalesShipmentLines.AsNoTracking().Where(x => x.SalesInvoiceLineId == line.Id
                && (x.Shipment!.Status == ShipmentStatus.Shipped || x.Shipment.Status == ShipmentStatus.Pending)).Select(x => new { x.QtyShipped, x.Shipment!.Status }).ToListAsync();
            fulfilled = Math.Min(qty, movements.Where(x => x.Status == ShipmentStatus.Shipped).Sum(x => x.QtyShipped));
            reserved = movements.Where(x => x.Status == ShipmentStatus.Pending).Sum(x => x.QtyShipped);
            rootUnfulfilled = Math.Max(0, qty - fulfilled - corrections.Sum(x => x.UnfulfilledQuantity));
        }
        if (!line.ItemId.HasValue || await db.Items.AsNoTracking().AnyAsync(x => x.Id == line.ItemId && x.IsService)) rootUnfulfilled = 0;
        return new(qty, fulfilled, reserved, corrections.Sum(x => x.Quantity), corrections.Sum(x => x.UnfulfilledQuantity), otherCorrection, returned, otherReturned, rootUnfulfilled);
    }

    public static async Task<InvoiceLineBalance> PurchaseAsync(AppDbContext db, PurchaseInvoiceLine line, Guid? exceptDebit = null, Guid? exceptReturn = null, bool approvedFulfillmentOnly = false)
    {
        var qty = line.QuantityBilled;
        var corrections = await db.DebitNoteLines.AsNoTracking().Where(x => x.HeaderId != exceptDebit && x.PurchaseInvoiceLineId == line.Id
            && x.Header!.Status != DebitNoteStatus.Void && (x.Header.Status == DebitNoteStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .Select(x => new { x.Quantity, x.UnfulfilledQuantity }).ToListAsync();
        var returned = await db.VendorReturnLines.AsNoTracking().Where(x => x.HeaderId != exceptReturn && x.PurchaseInvoiceLineId == line.Id
            && x.Header!.Status != VendorReturnStatus.Void && x.Header.ReturnType != VendorReturnType.PaymentOnly
            && (x.Header.Status == VendorReturnStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .SumAsync(x => x.Quantity);
        decimal fulfilled, reserved, rootUnfulfilled, otherCorrection = 0, otherReturned = 0;
        if (line.PurchaseOrderLineId.HasValue)
        {
            var root = await db.PurchaseOrderLines.AsNoTracking().SingleAsync(x => x.Id == line.PurchaseOrderLineId.Value);
            var receipts = await db.GoodsReceipts.AsNoTracking().Where(x => !approvedFulfillmentOnly || !x.GLBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GLBatchId && b.Status == BatchStatus.Posted))
                .SelectMany(x => x.Lines).Where(x => x.PurchaseOrderLineId == root.Id).Select(x => new { x.Id, x.QuantityReceived }).ToListAsync();
            var physical = receipts.Sum(x => x.QuantityReceived);
            var allocated = await db.PurchaseInvoiceLines.AsNoTracking().Where(x => x.PurchaseOrderLineId == root.Id && x.Id != line.Id && x.GoodsReceiptLineId.HasValue).Select(x => x.GoodsReceiptLineId!.Value).ToListAsync();
            fulfilled = Math.Min(qty, Math.Max(0, physical - receipts.Where(x => allocated.Contains(x.Id)).Sum(x => x.QuantityReceived)));
            reserved = await db.GoodsReceiptDraftLines.AsNoTracking().Where(x => x.PurchaseOrderLineId == root.Id).SumAsync(x => x.QuantityReceived);
            var rootDebits = await db.DebitNoteLines.AsNoTracking().Where(x => x.HeaderId != exceptDebit && x.PurchaseOrderLineId == root.Id
                && x.Header!.Status != DebitNoteStatus.Void && (x.Header.Status == DebitNoteStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
                .Select(x => new { x.Quantity, x.UnfulfilledQuantity, x.PurchaseInvoiceLineId }).ToListAsync();
            otherCorrection = rootDebits.Sum(x => x.PurchaseInvoiceLineId.HasValue ? x.UnfulfilledQuantity : x.Quantity);
            otherReturned = await db.VendorReturnLines.AsNoTracking().Where(x => x.HeaderId != exceptReturn && x.PurchaseOrderLineId == root.Id && !x.PurchaseInvoiceLineId.HasValue
                && x.Header!.Status != VendorReturnStatus.Void && x.Header.ReturnType != VendorReturnType.PaymentOnly
                && (x.Header.Status == VendorReturnStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
                .SumAsync(x => x.Quantity);
            rootUnfulfilled = Math.Max(0, root.QuantityOrdered - physical - otherCorrection);
        }
        else
        {
            fulfilled = Math.Min(qty, await db.GoodsReceipts.AsNoTracking().Where(x => !approvedFulfillmentOnly || !x.GLBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GLBatchId && b.Status == BatchStatus.Posted))
                .SelectMany(x => x.Lines).Where(x => x.PurchaseInvoiceLineId == line.Id).SumAsync(x => x.QuantityReceived));
            reserved = await db.GoodsReceiptDraftLines.AsNoTracking().Where(x => x.PurchaseInvoiceLineId == line.Id).SumAsync(x => x.QuantityReceived);
            rootUnfulfilled = Math.Max(0, qty - fulfilled - corrections.Sum(x => x.UnfulfilledQuantity));
        }
        if (!line.ItemId.HasValue || await db.Items.AsNoTracking().AnyAsync(x => x.Id == line.ItemId && x.IsService)) rootUnfulfilled = 0;
        return new(qty, fulfilled, reserved, corrections.Sum(x => x.Quantity), corrections.Sum(x => x.UnfulfilledQuantity), otherCorrection, returned, otherReturned, rootUnfulfilled);
    }
}
