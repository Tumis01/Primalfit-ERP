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
        private readonly IDocumentNumberingService _numberingService;

        public PaymentService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            TransactionMappingService mappingService,
            IDocumentNumberingService numberingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _mappingService = mappingService;
            _numberingService = numberingService;
        }

        public async Task<string> SavePaymentAsync(CustomerPayment pay)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (pay.CompanyId == Guid.Empty) return "Security Error: No Company Context.";
            var routeError = await InvoiceRoutePolicy.ValidateAsync(ctx, pay.CompanyId, TransactionTemplateModule.AR,
                new InvoiceRoute { TemplateId = pay.CustomTransactionTypeId, DebitAccountId = pay.DepositToGlAccountId == Guid.Empty ? null : pay.DepositToGlAccountId, CreditAccountId = pay.CreditGlAccountId == Guid.Empty ? null : pay.CreditGlAccountId, DiscountAccountId = pay.DiscountGlAccountId });
            if (routeError != null) return routeError;
            var paymentCurrency = await ctx.CustomerPayments.Where(x => x.Id == pay.Id && x.CompanyId == pay.CompanyId && x.CustomerId == pay.CustomerId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            if (!paymentCurrency.HasValue && pay.Applications.Count > 0)
            {
                var sourceId = pay.Applications.First().InvoiceId;
                paymentCurrency = await ctx.SalesInvoices.Where(x => x.Id == sourceId && x.CompanyId == pay.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync()
                    ?? await ctx.SalesOrders.Where(x => x.Id == sourceId && x.CompanyId == pay.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            }
            paymentCurrency ??= await ctx.Customers.Where(x => x.Id == pay.CustomerId && x.CompanyId == pay.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            var paymentCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(ctx, pay.CompanyId, pay.CurrencyId, pay.ExchangeRate, paymentCurrency);
            if (paymentCurrencyError != null) return paymentCurrencyError;
            if (string.IsNullOrWhiteSpace(pay.Reference))
                pay.Reference = await _numberingService.GenerateAndReserveNextNumberAsync(pay.CompanyId, SequenceTargetType.AR_CustomerReceipt, pay.Date);
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
                    if (existingBatch?.Status == BatchStatus.Ready)
                        return "This payment is awaiting General Ledger review and cannot be modified.";

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
                    .FromSqlInterpolated($"SELECT * FROM [CustomerPayments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {paymentId}")
                    .Include(p => p.Applications)
                    .FirstOrDefaultAsync(p => p.Id == paymentId);

                if (pay == null) return "Payment not found.";
                if (pay.Applications.Any(x => x.AppliedAmount < 0 || x.CashDiscountTaken < 0)
                    || pay.Applications.Select(x => x.InvoiceId).Distinct().Count() != pay.Applications.Count)
                    return "Payment applications must have non-negative amounts and reference each invoice only once.";
                if (pay.Status != PaymentStatus.Draft)
                {
                    var existingBatch = pay.GLBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == pay.GLBatchId.Value && b.CompanyId == pay.CompanyId)
                        : null;
                    if (existingBatch?.Status == BatchStatus.Posted)
                        return "This payment has already been committed to the General Ledger and cannot be modified.";
                    if (existingBatch?.Status == BatchStatus.Ready)
                        return "This payment is already awaiting General Ledger review.";
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
                var dimensionWeights = new List<(Guid? SetId, decimal Weight)>();

                // 5. Process Applications, Discounts, and Balance Checks
                decimal totalDiscountsBase = 0;
                decimal totalArSettledBase = 0;
                decimal totalWithholdingBase = 0;

                foreach (var app in pay.Applications)
                {
                    var applicationLegStart = glLines.Count;
                    var documentInvoice = await ctx.SalesInvoices
                        .FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {app.InvoiceId}").Include(o => o.Lines)
                        .FirstOrDefaultAsync(o => o.Id == app.InvoiceId);
                    var orderInvoice = documentInvoice == null
                        ? await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == app.InvoiceId)
                        : null;
                    if (orderInvoice == null && documentInvoice == null) return "A payment references an invoice that no longer exists.";
                    if (documentInvoice != null && !(documentInvoice.InvoiceNumber.StartsWith("AR-LEGACY-", StringComparison.Ordinal) && documentInvoice.Status == SalesInvoiceStatus.Posted && !documentInvoice.GLBatchId.HasValue)
                        && !await NewInvoiceCorrectionService.IsPostedSalesAsync(ctx, documentInvoice))
                        return $"Invoice {documentInvoice.InvoiceNumber} must be posted before it can receive payment.";
                    if ((orderInvoice?.CompanyId ?? documentInvoice!.CompanyId) != pay.CompanyId
                        || (orderInvoice?.CustomerId ?? documentInvoice!.CustomerId) != pay.CustomerId
                        || (orderInvoice?.CurrencyId ?? documentInvoice!.CurrencyId) != pay.CurrencyId)
                        return "Each invoice must belong to the payment's company, customer and currency.";

                    Guid invoiceCompanyId = orderInvoice?.CompanyId ?? documentInvoice!.CompanyId;
                    Guid? invoiceProjectId = orderInvoice?.ProjectId ?? documentInvoice!.ProjectId;
                    decimal invoiceRate = (orderInvoice?.ExchangeRate ?? documentInvoice!.ExchangeRate) > 0
                        ? orderInvoice?.ExchangeRate ?? documentInvoice!.ExchangeRate : 1m;
                    string invoiceNumber = orderInvoice?.OrderNumber ?? documentInvoice!.InvoiceNumber;
                    decimal subTotal = orderInvoice != null
                        ? orderInvoice.Lines.Sum(l => l.Quantity * l.UnitPrice)
                        : documentInvoice!.Lines.Sum(l => l.Amount);
                    decimal discountPercentage = orderInvoice?.DiscountPercentage ?? documentInvoice!.DiscountPercentage;
                    decimal discountAmount = orderInvoice?.DiscountAmount ?? documentInvoice!.DiscountAmount;
                    Guid? invoiceTaxId = orderInvoice?.TaxId ?? documentInvoice!.TaxId;
                    decimal withholdingPercentage = orderInvoice?.WithholdingPercentage ?? documentInvoice!.WithholdingPercentage;
                    Guid? withholdingSalesGlAccountId = orderInvoice?.WithholdingSalesGlAccountId ?? documentInvoice!.WithholdingSalesGlAccountId;
                    Guid? invoiceDiscountGlAccountId = orderInvoice?.DiscountGlAccountId ?? documentInvoice!.DiscountGlAccountId;
                    Guid invoiceId = app.InvoiceId;
                    Guid? legacyOrderId = documentInvoice != null
                        && documentInvoice.InvoiceNumber.StartsWith("AR-LEGACY-", StringComparison.Ordinal)
                            ? documentInvoice.SalesOrderId
                            : null;
                    Guid? creditOrderId = orderInvoice?.Id ?? legacyOrderId;

                    app.ProjectId = invoiceProjectId;

                    decimal discountVal = discountPercentage > 0 ? subTotal * (discountPercentage / 100) : discountAmount;
                    decimal discountedSubTotal = subTotal - discountVal;

                    decimal taxAmount = 0;
                    if (invoiceTaxId.HasValue)
                    {
                        var tax = await ctx.Taxes.FindAsync(invoiceTaxId.Value);
                        if (tax != null) taxAmount = discountedSubTotal * (tax.Per / 100);
                    }

                    decimal grandTotal = discountedSubTotal + taxAmount;

                    decimal totalPaidSoFar = await (from pa in ctx.PaymentApplications
                                                    join p in ctx.CustomerPayments on pa.CustomerPaymentId equals p.Id
                                                     where (pa.InvoiceId == invoiceId || (legacyOrderId.HasValue && pa.InvoiceId == legacyOrderId.Value)) && p.Status == PaymentStatus.Posted
                                                        && (!p.GLBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == p.GLBatchId.Value && (b.Status == BatchStatus.Posted || b.Status == BatchStatus.Ready)))
                                                        && p.Id != pay.Id
                                                    select pa.AppliedAmount + pa.CashDiscountTaken + pa.WithholdingAmountForeign).SumAsync();

                    var balanceIds = creditOrderId.HasValue && creditOrderId.Value != invoiceId ? new[] { invoiceId, creditOrderId.Value } : new[] { invoiceId };
                    var credits = await InvoiceFinancialBalances.SalesCreditsAsync(ctx, balanceIds, includeReserved: true);
                    var refunds = await InvoiceFinancialBalances.SalesCashRefundsAsync(ctx, balanceIds);
                    var totalCredited = balanceIds.Sum(id => credits.GetValueOrDefault(id));
                    totalPaidSoFar = Math.Max(0, totalPaidSoFar - balanceIds.Sum(id => refunds.GetValueOrDefault(id)));

                    decimal netBalanceDue = grandTotal - totalPaidSoFar - totalCredited;

                    decimal invoiceWithholding = discountedSubTotal * (withholdingPercentage / 100m);
                    decimal withholdingPaidSoFar = await (from pa in ctx.PaymentApplications
                                                          join p in ctx.CustomerPayments on pa.CustomerPaymentId equals p.Id
                                                          where (pa.InvoiceId == invoiceId || (legacyOrderId.HasValue && pa.InvoiceId == legacyOrderId.Value)) && p.Status == PaymentStatus.Posted
                                                             && (!p.GLBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == p.GLBatchId.Value && (b.Status == BatchStatus.Posted || b.Status == BatchStatus.Ready)))
                                                             && p.Id != pay.Id
                                                          select pa.WithholdingAmountForeign).SumAsync();
                    decimal remainingWithholding = Math.Max(0, invoiceWithholding - withholdingPaidSoFar);
                    decimal remainingCashDue = Math.Max(0, netBalanceDue - remainingWithholding);

                    if (app.AppliedAmount + app.CashDiscountTaken > remainingCashDue + 0.01m)
                    {
                    return $"Post Error: Payment ({app.AppliedAmount:N4}) exceeds the remaining cash due ({remainingCashDue:N4}) for invoice {invoiceNumber}.";
                    }

                    app.WithholdingAmountForeign = remainingCashDue > 0
                        ? Math.Min(remainingWithholding, Math.Round(remainingWithholding * (app.AppliedAmount + app.CashDiscountTaken) / remainingCashDue, 4))
                        : 0;
                    if (app.WithholdingAmountForeign > 0)
                    {
                        if (!withholdingSalesGlAccountId.HasValue || withholdingSalesGlAccountId == Guid.Empty)
                            return "Posting Aborted: The invoice has a withholding tax but no sales receivable account is mapped.";
                        bool withholdingAccountIsValid = await ctx.SegChartOfAccounts.AnyAsync(a =>
                            a.Id == withholdingSalesGlAccountId.Value && a.CompanyId == invoiceCompanyId && a.IsActive);
                        if (!withholdingAccountIsValid)
                            return "Posting Aborted: The invoice withholding receivable account is invalid or inactive.";

                        decimal withholdingBase = Math.Round(app.WithholdingAmountForeign * invoiceRate, 2);
                        totalWithholdingBase += withholdingBase;
                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = withholdingSalesGlAccountId.Value,
                            Debit = withholdingBase,
                            Credit = 0,
                            ProjectId = invoiceProjectId,
                            Reference = $"Withholding Tax: {invoiceNumber}"
                        });
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
                                defaultAccountId: invoiceDiscountGlAccountId ?? Guid.Empty);
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
                            ProjectId = invoiceProjectId,
                            Reference = $"Disc Allowed: {invoiceNumber}"
                        });
                    }

                    decimal totalLineSettledForeign = app.AppliedAmount + app.CashDiscountTaken + app.WithholdingAmountForeign;
                    totalArSettledBase += Math.Round(totalLineSettledForeign * invoiceRate, 2);
                    var projectWeight = Math.Max(0m, Math.Round(totalLineSettledForeign * invoiceRate, 2));
                    var sourceWeights = orderInvoice != null
                        ? orderInvoice.Lines.Select(x => (SetId: x.DimensionSetId, Weight: Math.Abs(x.LineTotal))).ToList()
                        : documentInvoice!.Lines.Select(x => (SetId: x.DimensionSetId, Weight: Math.Abs(x.Amount))).ToList();
                    sourceWeights = await DimensionService.PostedInvoiceWeightsAsync(ctx, pay.CompanyId,
                        documentInvoice?.GLBatchId ?? orderInvoice?.InvoiceBatchId, true, "AR " + invoiceNumber, sourceWeights);
                    if (app.DimensionSetId.HasValue) sourceWeights = new() { (app.DimensionSetId, 1m) };
                    if (sourceWeights.Count == 0) sourceWeights.Add((null, 1m));
                    var weightTotal = sourceWeights.Sum(x => x.Weight);
                    if (weightTotal <= 0) weightTotal = 1;
                    dimensionWeights.AddRange(sourceWeights.Select(x => (x.SetId, projectWeight * x.Weight / weightTotal)));
                    var applicationLegs = glLines.Skip(applicationLegStart).ToList();
                    glLines.RemoveRange(applicationLegStart, glLines.Count - applicationLegStart);
                    DimensionService.AllocateSummaryLines(applicationLegs, sourceWeights);
                    glLines.AddRange(applicationLegs);

                    decimal finalRemaining = netBalanceDue - totalLineSettledForeign;
                    if (orderInvoice != null)
                        orderInvoice.Status = finalRemaining <= 0.01m ? OrderStatus.Invoiced : OrderStatus.PartiallyInvoiced;
                    else
                        documentInvoice!.IsPaid = finalRemaining <= 0.01m;
                }

                // 6. CREDIT ACCOUNTS RECEIVABLE (Asset Decreases)
                decimal finalArCreditBase = totalBankBase + totalDiscountsBase + totalWithholdingBase;
                var controlLines = new List<GLJournalLine> {
                    new() { SegCoaId = bankAccount, Debit = totalBankBase, Reference = $"Rcpt {pay.Reference}" },
                    new() { SegCoaId = arAccount, Credit = finalArCreditBase, Reference = $"Pay Inv {pay.Reference}" }
                };
                DimensionService.AllocateSummaryLines(controlLines, dimensionWeights);
                glLines.AddRange(controlLines);

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
