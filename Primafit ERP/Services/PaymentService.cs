using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PaymentService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;

        public PaymentService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
        }

        public async Task<string> SavePaymentAsync(CustomerPayment pay)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (pay.CompanyId == Guid.Empty) return "Security Error: No Company Context.";
            if (pay.DepositToGlAccountId == Guid.Empty) return "Please select a Bank (Deposit) Account.";
            if (pay.CreditGlAccountId == Guid.Empty) return "Customer AR Account is missing.";

            if (pay.Id == Guid.Empty || !await ctx.CustomerPayments.AnyAsync(x => x.Id == pay.Id))
            {
                if (pay.Id == Guid.Empty) pay.Id = Guid.NewGuid();
                ctx.CustomerPayments.Add(pay);
            }
            else
            {
                var existing = await ctx.CustomerPayments.Include(p => p.Applications).FirstOrDefaultAsync(p => p.Id == pay.Id);
                if (existing != null)
                {
                    if (existing.Status != PaymentStatus.Draft) return "Cannot edit a posted payment.";
                    ctx.Entry(existing).CurrentValues.SetValues(pay);
                    ctx.PaymentApplications.RemoveRange(existing.Applications);
                    foreach (var app in pay.Applications) ctx.PaymentApplications.Add(app);
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> PostPaymentAsync(Guid paymentId, string userId)
{
    using var ctx = await _dbFactory.CreateDbContextAsync();
    using var transaction = await ctx.Database.BeginTransactionAsync();

    try
    {
        var pay = await ctx.CustomerPayments
            .Include(p => p.Applications)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (pay == null) return "Payment not found.";
        if (pay.Status != PaymentStatus.Draft) return "Only draft payments can be posted.";

        // Validate Seg COA existence
        bool bankExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == pay.DepositToGlAccountId && a.CompanyId == pay.CompanyId && a.IsActive);
        if (!bankExists) return "Deposit Bank Account is invalid (not in Seg COA).";

        bool arExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == pay.CreditGlAccountId && a.CompanyId == pay.CompanyId && a.IsActive);
        if (!arExists) return "Customer AR Account is invalid (not in Seg COA).";

        var glLines = new List<GLJournalLine>();

        // 1. DEBIT BANK (Asset Increases)
        decimal totalBankBase = Math.Round(pay.AmountReceived * pay.ExchangeRate, 2);

        glLines.Add(new GLJournalLine
        {
            SegCoaId = pay.DepositToGlAccountId,
            Debit = totalBankBase,
            Credit = 0,
            Reference = $"Rcpt {pay.Reference}"
        });

        // 2. CREDIT ACCOUNTS RECEIVABLE (Asset Decreases)
        foreach (var app in pay.Applications)
        {
            var invoice = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == app.InvoiceId);
            if (invoice == null) continue;

            decimal invoiceRate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1;
            decimal arClearedBase = Math.Round(app.AppliedAmount * invoiceRate, 2);

            // --- PARTIAL PAYMENT, DISCOUNT, & CREDIT NOTE TRACKING ---
            decimal subTotal = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice);

            decimal discountValue = invoice.DiscountAmount;
            if (invoice.DiscountPercentage > 0)
            {
                discountValue = subTotal * (invoice.DiscountPercentage / 100);
            }
            decimal discountedSubTotal = subTotal - discountValue;

            decimal taxAmount = 0;
            if (invoice.TaxId.HasValue)
            {
                var tax = await ctx.Taxes.FindAsync(invoice.TaxId);
                if (tax != null) taxAmount = discountedSubTotal * (tax.Per / 100);
            }

            decimal grandTotal = discountedSubTotal + taxAmount;

            // FIXED: Inner join constraints isolate and calculate strictly historically POSTED payments
            decimal totalPaidSoFar = await (from pa in ctx.PaymentApplications
                                            join p in ctx.CustomerPayments on pa.CustomerPaymentId equals p.Id
                                            where pa.InvoiceId == invoice.Id && p.Status == PaymentStatus.Posted
                                            select pa.AppliedAmount + pa.CashDiscountTaken).SumAsync();

            decimal totalCredited = await ctx.CreditNotes
                .Where(cn => cn.SalesOrderId == invoice.Id && cn.Status == CreditNoteStatus.Posted)
                .SumAsync(cn => cn.TotalAmount);

            decimal netBalanceDue = grandTotal - totalPaidSoFar - totalCredited;

            if (app.AppliedAmount > netBalanceDue + 0.01m)
            {
                return $"Post Error: Applied payment amount ({app.AppliedAmount:N2}) for invoice {invoice.OrderNumber} exceeds the remaining adjusted balance due ({netBalanceDue:N2}) after credit note reductions.";
            }

            decimal finalRemainingBalance = netBalanceDue - app.AppliedAmount;
            if (finalRemainingBalance <= 0.01m)
            {
                invoice.Status = OrderStatus.Invoiced;
            }
            else
            {
                invoice.Status = OrderStatus.PartiallyInvoiced;
            }
        }

        // Credit the AR Account
        glLines.Add(new GLJournalLine
        {
            SegCoaId = pay.CreditGlAccountId,
            Debit = 0,
            Credit = totalBankBase,
            Reference = $"Pay Inv {pay.Reference}"
        });

        decimal totalDebits = glLines.Sum(x => x.Debit);
        decimal totalCredits = glLines.Sum(x => x.Credit);

        if (totalDebits != totalCredits)
        {
            return $"Balance Error: Debits ({totalDebits}) do not equal Credits ({totalCredits}).";
        }

        var (err, batchId) = await _glOps.CreateJournalEntryAsync(
            pay.CompanyId,
            DateOnly.FromDateTime(pay.Date),
            "Customer Receipt",
            $"Rcpt {pay.Reference}",
            glLines,
            userId);

        if (!string.IsNullOrEmpty(err)) throw new Exception(err);

        if (batchId.HasValue) await _glOps.PostBatchAsync(pay.CompanyId, batchId.Value, userId);

        pay.Status = PaymentStatus.Posted;
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
    }
}