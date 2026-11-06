using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

internal static class InvoiceFinancialBalances
{
    public static async Task<Dictionary<Guid, decimal>> SalesCreditsAsync(AppDbContext db, IEnumerable<Guid> invoiceIds, bool includeReserved = false, DateOnly? asOfDate = null)
    {
        var ids = invoiceIds.Distinct().ToList();
        var rows = await db.CreditNotes.AsNoTracking().Where(x => x.ReturnToStock && (!asOfDate.HasValue || x.Date <= asOfDate.Value) && (x.SalesInvoiceId.HasValue || x.SalesOrderId.HasValue)
            && ids.Contains((x.SalesInvoiceId ?? x.SalesOrderId)!.Value)
            && ((includeReserved && x.Status == CreditNoteStatus.Draft && x.SalesInvoiceId.HasValue)
                || (x.Status == CreditNoteStatus.Posted && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && (b.Status == BatchStatus.Posted || (includeReserved && b.Status == BatchStatus.Ready)))))))
            .Select(x => new { Id = (x.SalesInvoiceId ?? x.SalesOrderId)!.Value, x.TotalAmount }).ToListAsync();
        return rows.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Sum(y => y.TotalAmount));
    }

    public static async Task<Dictionary<Guid, decimal>> SalesCashRefundsAsync(AppDbContext db, IEnumerable<Guid> invoiceIds, DateOnly? asOfDate = null)
    {
        var ids = invoiceIds.Distinct().ToList();
        var returns = await db.ReceiptRefunds.AsNoTracking().Where(x => (!asOfDate.HasValue || x.Date <= asOfDate.Value) && (x.SalesInvoiceId.HasValue || x.SalesOrderId.HasValue)
            && ids.Contains((x.SalesInvoiceId ?? x.SalesOrderId)!.Value) && x.Status == ReceiptRefundStatus.Posted
            && (x.RefundType == ReceiptRefundType.PaymentOnly || x.RefundType == ReceiptRefundType.Both)
            && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && b.Status == BatchStatus.Posted)))
            .Select(x => new { Id = (x.SalesInvoiceId ?? x.SalesOrderId)!.Value, x.TotalAmount }).ToListAsync();
        var credits = await db.CreditNotes.AsNoTracking().Where(x => !x.ReturnToStock && (!asOfDate.HasValue || x.Date <= asOfDate.Value) && (x.SalesInvoiceId.HasValue || x.SalesOrderId.HasValue)
            && ids.Contains((x.SalesInvoiceId ?? x.SalesOrderId)!.Value) && x.Status == CreditNoteStatus.Posted
            && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && b.Status == BatchStatus.Posted)))
            .Select(x => new { Id = (x.SalesInvoiceId ?? x.SalesOrderId)!.Value, x.TotalAmount }).ToListAsync();
        return returns.Concat(credits).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Sum(y => y.TotalAmount));
    }

    public static async Task<decimal> PurchaseDebitBaseAsync(AppDbContext db, PurchaseInvoice invoice, bool includeReserved = false)
    {
        var legacyOrderId = invoice.PurchaseInvoiceNumber.StartsWith("PINV-LEGACY-", StringComparison.Ordinal) ? invoice.PurchaseOrderId : null;
        var rows = await db.DebitNotes.AsNoTracking().Where(x => x.CompanyId == invoice.CompanyId
            && (x.PurchaseInvoiceId == invoice.Id || (legacyOrderId.HasValue && !x.PurchaseInvoiceId.HasValue && x.PurchaseOrderId == legacyOrderId))
            && ((includeReserved && x.Status == DebitNoteStatus.Draft && x.PurchaseInvoiceId.HasValue)
                || (x.Status == DebitNoteStatus.Posted && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && (b.Status == BatchStatus.Posted || (includeReserved && b.Status == BatchStatus.Ready)))))))
            .Select(x => new { x.TotalAmount, x.ExchangeRate }).ToListAsync();
        return rows.Sum(x => Math.Round(x.TotalAmount * (x.ExchangeRate > 0 ? x.ExchangeRate : 1), 2));
    }

    public static Task<decimal> PurchaseCashRefundedAsync(AppDbContext db, Guid invoiceId) =>
        db.VendorReturns.AsNoTracking().Where(x => x.PurchaseInvoiceId == invoiceId && x.Status == VendorReturnStatus.Posted
            && (x.ReturnType == VendorReturnType.PaymentOnly || x.ReturnType == VendorReturnType.Both)
            && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && b.Status == BatchStatus.Posted)))
            .SumAsync(x => x.TotalAmount);
}
