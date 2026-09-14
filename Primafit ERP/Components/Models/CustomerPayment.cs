using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{


    public class CustomerPayment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required]
        public Guid CompanyId { get; set; }
        public Guid CustomerId { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public string Reference { get; set; } = string.Empty; // e.g., Check # or Transfer Ref

        // --- CORE GL ACCOUNTS ---
        public Guid DepositToGlAccountId { get; set; } // Bank Account (Debit)
        public Guid CreditGlAccountId { get; set; }    // AR Account (Credit)

        // --- ADVANCED GL ACCOUNTS (For Edge Cases) ---
        public Guid? DiscountGlAccountId { get; set; }      // Expense/Contra-Revenue for Cash Discounts
        public Guid? FxGainLossGlAccountId { get; set; }    // Income/Expense for Exchange Rate differences
        public Guid? UnappliedCashGlAccountId { get; set; } // Liability account for Customer Overpayments

        // --- CURRENCY & AMOUNTS ---
        [Column(TypeName = "decimal(18,4)")]
        public decimal AmountReceived { get; set; } // Amount in Foreign Currency

        public Guid CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1; // Rate on the day of payment
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public CustomTransactionType? CustomTransactionType { get; set; }

        // --- STATUS ---
        public PaymentStatus Status { get; set; } = PaymentStatus.Draft;
        public Guid? GLBatchId { get; set; }

        // --- INVOICES PAID ---
        public List<PaymentApplication> Applications { get; set; } = new();
    }

    public class PaymentApplication
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomerPaymentId { get; set; }

        // Links to the Sales Order that was Invoiced
        public Guid InvoiceId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal AppliedAmount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal CashDiscountTaken { get; set; }
    }
}
