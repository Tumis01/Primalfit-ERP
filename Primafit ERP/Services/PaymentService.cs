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
            // Note: CreditGlAccountId (AR) might be empty if customer config is missing, catch in UI or here.
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

        public async Task<string> PostPaymentAsync(Guid paymentId)
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
                bool bankExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == pay.DepositToGlAccountId && a.CompanyId == pay.CompanyId);
                if (!bankExists) return "Deposit Bank Account is invalid (not in Seg COA).";

                bool arExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == pay.CreditGlAccountId && a.CompanyId == pay.CompanyId);
                if (!arExists) return "Customer AR Account is invalid (not in Seg COA).";

                var glLines = new List<GLJournalLine>();

                // 1. DEBIT BANK (Asset Increases)
                decimal totalBankBase = Math.Round(pay.AmountReceived * pay.ExchangeRate, 2);

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = pay.DepositToGlAccountId, // CORRECTED PROPERTY
                    Debit = totalBankBase,
                    Credit = 0,
                    Reference = $"Rcpt {pay.Reference}"
                });

                // 2. CREDIT ACCOUNTS RECEIVABLE (Asset Decreases)
                decimal totalArCredit = 0;

                foreach (var app in pay.Applications)
                {
                    var invoice = await ctx.SalesOrders.FindAsync(app.InvoiceId);
                    if (invoice == null) continue;

                    decimal invoiceRate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1;

                    // The AR amount we are clearing in BASE currency
                    decimal arClearedBase = Math.Round(app.AppliedAmount * invoiceRate, 2);
                    totalArCredit += arClearedBase;

                    // FX Logic (Simplified placeholder)
                    // decimal cashValueForThisInvoice = Math.Round(app.AppliedAmount * pay.ExchangeRate, 2);
                    // decimal fxDiff = cashValueForThisInvoice - arClearedBase;
                }

                // Credit the AR Account
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = pay.CreditGlAccountId, // CORRECTED PROPERTY
                    Debit = 0,
                    Credit = totalArCredit,
                    Reference = $"Pay Inv {pay.Reference}"
                });

                // Simple Imbalance Check (FX variances need a dedicated line in a real scenario)
                decimal totalDebits = glLines.Sum(x => x.Debit);
                decimal totalCredits = glLines.Sum(x => x.Credit);

                if (totalDebits != totalCredits)
                {
                    return $"Balance Error: Debits ({totalDebits}) do not equal Credits ({totalCredits}). FX Variance handling required.";
                }

                // POST using GL Service
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    pay.CompanyId,
                    DateOnly.FromDateTime(pay.Date),
                    "Customer Receipt",
                    $"Rcpt {pay.Reference}",
                    glLines);

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue) await _glOps.PostBatchAsync(pay.CompanyId, batchId.Value);

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