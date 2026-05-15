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

        // Added 'string userId' to the parameters here
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
                decimal totalArCredit = 0;

                foreach (var app in pay.Applications)
                {
                    var invoice = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == app.InvoiceId);
                    if (invoice == null) continue;

                    decimal invoiceRate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1;
                    decimal arClearedBase = Math.Round(app.AppliedAmount * invoiceRate, 2);
                    totalArCredit += arClearedBase;

                    // --- PARTIAL PAYMENT & DISCOUNT TRACKING ---
                    // 1. Calculate SubTotal
                    decimal subTotal = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice);

                    // 2. Apply Discount
                    decimal discountValue = invoice.DiscountAmount;
                    if (invoice.DiscountPercentage > 0)
                    {
                        discountValue = subTotal * (invoice.DiscountPercentage / 100);
                    }
                    decimal discountedSubTotal = subTotal - discountValue;

                    // 3. Apply Tax to Discounted SubTotal
                    decimal taxAmount = 0;
                    if (invoice.TaxId.HasValue)
                    {
                        var tax = await ctx.Taxes.FindAsync(invoice.TaxId);
                        if (tax != null) taxAmount = discountedSubTotal * (tax.Per / 100);
                    }

                    // 4. Final Grand Total
                    decimal grandTotal = discountedSubTotal + taxAmount;

                    // 5. Check if Fully Paid
                    decimal totalPaidSoFar = await ctx.PaymentApplications
                        .Where(a => a.InvoiceId == invoice.Id)
                        .SumAsync(a => a.AppliedAmount + a.CashDiscountTaken);

                    // Add CURRENT application
                    totalPaidSoFar += app.AppliedAmount;
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

                // Pass the incoming userId parameter to the GL Ops
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    pay.CompanyId,
                    DateOnly.FromDateTime(pay.Date),
                    "Customer Receipt",
                    $"Rcpt {pay.Reference}",
                    glLines,
                    userId); // Used parameter here

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                // Pass the incoming userId parameter here as well
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