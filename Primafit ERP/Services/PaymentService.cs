using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class PaymentService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly TransactionMappingService _mappingService;

        public PaymentService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _mappingService = mappingService;
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
                    var batchId = pay.GLBatchId ?? existing.GLBatchId;
                    var existingBatch = batchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId.Value && b.CompanyId == existing.CompanyId)
                        : null;

                    if (existingBatch?.Status == BatchStatus.Posted)
                        return "This payment has already been committed to the General Ledger and cannot be modified.";

                    if (existing.Status != PaymentStatus.Draft && existingBatch == null)
                        return "Only draft payments or payments with an active review batch can be edited.";

                    pay.GLBatchId = batchId;
                    ctx.Entry(existing).CurrentValues.SetValues(pay);
                    ctx.PaymentApplications.RemoveRange(existing.Applications);
                    foreach (var app in pay.Applications)
                    {
                        app.Id = Guid.NewGuid();
                        app.CustomerPaymentId = pay.Id;
                        ctx.PaymentApplications.Add(app);
                    }
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
                if (pay.Status != PaymentStatus.Draft)
                {
                    var existingBatch = pay.GLBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == pay.GLBatchId.Value && b.CompanyId == pay.CompanyId)
                        : null;
                    if (existingBatch?.Status == BatchStatus.Posted)
                        return "This payment has already been committed to the General Ledger and cannot be modified.";
                    if (existingBatch == null)
                        return "Only draft payments can be submitted for review.";
                }

                // 1. Resolve custom mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (pay.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == pay.CompanyId && m.CustomTransactionTypeId == pay.CustomTransactionTypeId.Value);
                }

                // 2. Resolve Bank Deposit Account (Debit)
                Guid bankAccount = pay.DepositToGlAccountId != Guid.Empty
                    ? pay.DepositToGlAccountId
                    : customMapping?.OverrideDebitGlAccountId ?? Guid.Empty;

                if (bankAccount == Guid.Empty)
                {
                    bankAccount = await _mappingService.GetMappedAccountAsync(
                        pay.CompanyId,
                        SystemTransactionType.CustomerPayment,
                        isDebit: true,
                        defaultAccountId: Guid.Empty);
                }

                // 3. Resolve AR Account (Credit)
                Guid arAccount = pay.CreditGlAccountId != Guid.Empty
                    ? pay.CreditGlAccountId
                    : customMapping?.OverrideCreditGlAccountId ?? Guid.Empty;

                if (arAccount == Guid.Empty)
                {
                    var customer = await ctx.Customers.FindAsync(pay.CustomerId);
                    Guid defaultAr = customer?.ReceivablesAccountId ?? Guid.Empty;

                    arAccount = await _mappingService.GetMappedAccountAsync(
                        pay.CompanyId,
                        SystemTransactionType.CustomerPayment,
                        isDebit: false,
                        defaultAccountId: defaultAr);
                }

                bool bankExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == bankAccount && a.CompanyId == pay.CompanyId && a.IsActive);
                if (!bankExists) return "Deposit Bank Account is invalid or inactive.";

                bool arExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == arAccount && a.CompanyId == pay.CompanyId && a.IsActive);
                if (!arExists) return "Customer AR Account is invalid or inactive.";

                var glLines = new List<GLJournalLine>();
                decimal rate = pay.ExchangeRate > 0 ? pay.ExchangeRate : 1m;

                // 4. DEBIT BANK (Liquid Asset Increases)
                decimal totalBankBase = Math.Round(pay.AmountReceived * rate, 2);

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = bankAccount,
                    Debit = totalBankBase,
                    Credit = 0,
                    Reference = $"Rcpt {pay.Reference}"
                });

                // 5. Process Applications, Discounts, and Balance Checks
                decimal totalDiscountsBase = 0;
                decimal totalArSettledBase = 0;

                foreach (var app in pay.Applications)
                {
                    var invoice = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == app.InvoiceId);
                    if (invoice == null) continue;

                    decimal invoiceRate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1;

                    decimal subTotal = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice);
                    decimal discountVal = invoice.DiscountPercentage > 0 ? subTotal * (invoice.DiscountPercentage / 100) : invoice.DiscountAmount;
                    decimal discountedSubTotal = subTotal - discountVal;

                    decimal taxAmount = 0;
                    if (invoice.TaxId.HasValue)
                    {
                        var tax = await ctx.Taxes.FindAsync(invoice.TaxId);
                        if (tax != null) taxAmount = discountedSubTotal * (tax.Per / 100);
                    }

                    decimal grandTotal = discountedSubTotal + taxAmount;

                    decimal totalPaidSoFar = await (from pa in ctx.PaymentApplications
                                                    join p in ctx.CustomerPayments on pa.CustomerPaymentId equals p.Id
                                                     where pa.InvoiceId == invoice.Id && p.Status == PaymentStatus.Posted
                                                        && (!p.GLBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == p.GLBatchId.Value && b.Status == BatchStatus.Posted))
                                                        && p.Id != pay.Id
                                                    select pa.AppliedAmount + pa.CashDiscountTaken).SumAsync();

                    decimal totalCredited = await (from cn in ctx.CreditNotes
                                                   where cn.SalesOrderId == invoice.Id && cn.Status == CreditNoteStatus.Posted
                                                      && (!cn.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == cn.GlBatchId.Value && b.Status == BatchStatus.Posted))
                                                   select cn.TotalAmount).SumAsync();

                    decimal netBalanceDue = grandTotal - totalPaidSoFar - totalCredited;

                    if (app.AppliedAmount > netBalanceDue + 0.01m)
                    {
                    return $"Post Error: Applied payment ({app.AppliedAmount:N4}) for invoice {invoice.OrderNumber} exceeds remaining balance ({netBalanceDue:N4}).";
                    }

                    // Cash discount taken on receipt
                    if (app.CashDiscountTaken > 0)
                    {
                        Guid discountAllowedGl = pay.DiscountGlAccountId ?? Guid.Empty;

                        if (discountAllowedGl == Guid.Empty)
                        {
                            discountAllowedGl = await _mappingService.GetMappedAccountAsync(
                                pay.CompanyId,
                                SystemTransactionType.DiscountAllowed,
                                isDebit: true,
                                defaultAccountId: invoice.DiscountGlAccountId ?? Guid.Empty);
                        }

                        if (discountAllowedGl == Guid.Empty)
                            return "Posting Aborted: Cash discount is taken, but no Discount Allowed GL Account could be resolved.";

                        decimal discountTakenBase = Math.Round(app.CashDiscountTaken * invoiceRate, 2);
                        totalDiscountsBase += discountTakenBase;

                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = discountAllowedGl,
                            Debit = discountTakenBase,
                            Credit = 0,
                            Reference = $"Disc Allowed: {invoice.OrderNumber}"
                        });
                    }

                    decimal totalLineSettledForeign = app.AppliedAmount + app.CashDiscountTaken;
                    totalArSettledBase += Math.Round(totalLineSettledForeign * invoiceRate, 2);

                    decimal finalRemaining = netBalanceDue - totalLineSettledForeign;
                    invoice.Status = finalRemaining <= 0.01m ? OrderStatus.Invoiced : OrderStatus.PartiallyInvoiced;
                }

                // 6. CREDIT ACCOUNTS RECEIVABLE (Asset Decreases)
                decimal finalArCreditBase = totalBankBase + totalDiscountsBase;

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = arAccount,
                    Debit = 0,
                    Credit = finalArCreditBase,
                    Reference = $"Pay Inv {pay.Reference}"
                });

                decimal totalDebits = glLines.Sum(x => x.Debit);
                decimal totalCredits = glLines.Sum(x => x.Credit);

                if (totalDebits != totalCredits)
                {
                    return $"Balance Error: Debits ({totalDebits:N4}) do not equal Credits ({totalCredits:N4}).";
                }

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    pay.CompanyId,
                    DateOnly.FromDateTime(pay.Date),
                    "Customer Receipt",
                    $"Rcpt {pay.Reference}",
                    glLines,
                    userId,
                    existingBatchId: pay.GLBatchId);

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(pay.CompanyId, batchId.Value, userId);

                pay.DepositToGlAccountId = bankAccount;
                pay.CreditGlAccountId = arAccount;
                pay.GLBatchId = batchId;
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
