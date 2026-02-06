using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class CustomerPayment
    {
        public Guid Id { get; set; }
        [Required]
        public Guid CompanyId { get; set; }
        public Guid CustomerId { get; set; }
        public DateTime Date { get; set; }
        public Guid DepositToGlAccountId { get; set; }
        public Guid CreditGlAccountId { get; set; }

        public decimal AmountReceived { get; set; }
        public decimal ExchangeRate { get; set; } // For Multi-currency
    }

    public class PaymentApplication
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CustomerPaymentId { get; set; }

        // Links to the Sales Order / Invoice you are paying off
        public Guid InvoiceId { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal AppliedAmount { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal CashDiscountTaken { get; set; }
    }
}