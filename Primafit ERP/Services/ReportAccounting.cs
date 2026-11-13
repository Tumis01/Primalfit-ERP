using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

/// <summary>Accounting boundaries shared by financial and operational reports.</summary>
public static class ReportAccounting
{
    public static IQueryable<GLTransaction> PostedTransactions(AppDbContext db, Guid companyId) =>
        db.GLTransactions.AsNoTracking().Where(t => t.CompanyId == companyId
            && db.GLBatches.Any(b => b.Id == t.BatchId && b.CompanyId == companyId && (b.Status == BatchStatus.Posted || (b.PostedAt.HasValue && t.CreatedAt <= b.PostedAt.Value))));

    public static IQueryable<PurchaseInvoice> PostedBills(AppDbContext db, Guid companyId) =>
        db.PurchaseInvoices.AsNoTracking().Where(b => b.CompanyId == companyId && b.IsPosted
            && (!b.GLBatchId.HasValue || db.GLBatches.Any(g => g.Id == b.GLBatchId && g.CompanyId == companyId && (g.Status == BatchStatus.Posted || g.PostedAt.HasValue))));

    public static IQueryable<GoodsReceiptLine> PostedReceiptLines(AppDbContext db, Guid companyId) =>
        db.GoodsReceiptLines.AsNoTracking().Where(l => db.GoodsReceipts.Any(r => r.Id == l.GoodsReceiptId && r.CompanyId == companyId
            && r.GLBatchId.HasValue && db.GLBatches.Any(b => b.Id == r.GLBatchId && b.CompanyId == companyId && (b.Status == BatchStatus.Posted || b.PostedAt.HasValue))));

    public static IQueryable<VendorPayment> PostedVendorPayments(AppDbContext db, Guid companyId) =>
        db.Set<VendorPayment>().AsNoTracking().Where(p => db.PurchaseInvoices.Any(i => i.Id == p.PurchaseInvoiceId && i.CompanyId == companyId)
            && !p.Reference.StartsWith("draft-")
            && (!p.GLBatchId.HasValue || db.GLBatches.Any(b => b.Id == p.GLBatchId && b.CompanyId == companyId && (b.Status == BatchStatus.Posted || b.PostedAt.HasValue))));

    public static void ValidatePeriod(DateOnly from, DateOnly to)
    {
        if (from > to) throw new ArgumentException("The start date must be on or before the end date.");
    }

    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static async Task<Dictionary<Guid, decimal>> LegacySalesTotalsAsync(AppDbContext db, Guid companyId, IEnumerable<SalesOrder> orders)
    {
        var documents = orders.ToList();
        var batchIds = documents.Where(x => x.InvoiceBatchId.HasValue).Select(x => x.InvoiceBatchId!.Value).Distinct().ToList();
        var entries = await PostedTransactions(db, companyId).Where(x => batchIds.Contains(x.BatchId)).ToListAsync();
        var customerIds = documents.Select(x => x.CustomerId).Distinct().ToList();
        var customerAccounts = await db.Customers.Where(x => x.CompanyId == companyId && customerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ReceivablesAccountId);
        var totals = new Dictionary<Guid, decimal>();
        foreach (var order in documents)
        {
            var account = order.ReceivablesGlAccountId ?? customerAccounts.GetValueOrDefault(order.CustomerId);
            var rows = entries.Where(x => x.BatchId == order.InvoiceBatchId && x.SegCoaId == account).ToList();
            if (rows.Count == 0 && order.Lines.Any(x => x.Quantity * x.UnitPrice != 0))
                throw new ArgumentException($"Legacy invoice {order.OrderNumber} has no posted receivables snapshot. Restore its posting before reporting its balance.");
            totals[order.Id] = rows.Sum(x => x.Debit - x.Credit) / (order.ExchangeRate > 0 ? order.ExchangeRate : 1);
        }
        return totals;
    }
}
