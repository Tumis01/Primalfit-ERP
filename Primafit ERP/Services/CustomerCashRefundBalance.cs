using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services;

// Cash refunds can be issued through either customer-return document. Both
// paths must consume the same approved cash balance under the source-row lock.
internal static class CustomerCashRefundBalance
{
    public static async Task<(decimal Paid, decimal Claimed)> GetAsync(AppDbContext db, Guid companyId, Guid customerId, Guid sourceId, Guid? exceptRefundId = null, Guid? exceptCreditId = null)
    {
        var paid = await (from application in db.PaymentApplications
            join payment in db.CustomerPayments on application.CustomerPaymentId equals payment.Id
            where application.InvoiceId == sourceId && payment.CompanyId == companyId && payment.CustomerId == customerId
                && payment.Status == PaymentStatus.Posted
                && (!payment.GLBatchId.HasValue || db.GLBatches.Any(b => b.Id == payment.GLBatchId.Value && b.Status == BatchStatus.Posted))
            select application.AppliedAmount).SumAsync();
        var returns = await db.ReceiptRefunds.Where(x => x.CompanyId == companyId && x.SalesOrderId == sourceId && x.Id != exceptRefundId
                && x.Status == ReceiptRefundStatus.Posted && (x.RefundType == ReceiptRefundType.PaymentOnly || x.RefundType == ReceiptRefundType.Both)
                && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .SumAsync(x => x.TotalAmount);
        var credits = await db.CreditNotes.Where(x => x.CompanyId == companyId && x.SalesOrderId == sourceId && x.Id != exceptCreditId
                && !x.ReturnToStock && x.Status == CreditNoteStatus.Posted
                && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .SumAsync(x => x.TotalAmount);
        return (paid, returns + credits);
    }
}
