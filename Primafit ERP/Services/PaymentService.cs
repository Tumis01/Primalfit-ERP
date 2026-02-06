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
            if (pay.DepositToGlAccountId == Guid.Empty || pay.CreditGlAccountId == Guid.Empty)
                return "Please select both Deposit (Debit) and Credit accounts.";

            if (pay.Id == Guid.Empty || !await ctx.CustomerPayments.AnyAsync(x => x.Id == pay.Id))
            {
                ctx.CustomerPayments.Add(pay);
            }
            else
            {
                ctx.CustomerPayments.Update(pay);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // === POSTING LOGIC ===
        public async Task<string> PostPaymentAsync(Guid paymentId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var pay = await ctx.CustomerPayments.FindAsync(paymentId);
            if (pay == null) return "Payment not found.";

            var glLines = new List<GLJournalLine>();

            // 1. DEBIT BANK (Increase Cash) - User Selected
            glLines.Add(new GLJournalLine
            {
                AccountId = pay.DepositToGlAccountId,
                Debit = pay.AmountReceived,
                Credit = 0,
                Reference = "Customer Payment"
            });

            // 2. CREDIT RECEIVABLE (Reduce Debt) - User Selected
            glLines.Add(new GLJournalLine
            {
                AccountId = pay.CreditGlAccountId,
                Debit = 0,
                Credit = pay.AmountReceived, // Assumes simple posting, Exchange Gain/Loss logic adds complexity here
                Reference = "Payment Applied"
            });

            // 3. SEND TO GL ENGINE
            var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                pay.CompanyId,
                DateOnly.FromDateTime(pay.Date),
                "Customer Receipt",
                $"Rcpt {pay.Id.ToString().Substring(0, 8)}",
                glLines
            );

            return err;
        }
    }
}