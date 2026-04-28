using System.ComponentModel.DataAnnotations;

namespace PrimafitERP.Api.DTOs
{
    // --- CUSTOMER RECEIPTS (PAYMENTS) ---
    public class CreateCustomerPaymentDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid CustomerId { get; set; }
        [Required] public Guid DepositToGlAccountId { get; set; } // Bank Account
        [Required] public Guid CreditGlAccountId { get; set; } // Accounts Receivable
        [Required] public decimal AmountReceived { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string Reference { get; set; } = string.Empty;

        [Required] public List<PaymentApplicationDto> Applications { get; set; } = new();
    }

    public class PaymentApplicationDto
    {
        [Required] public Guid InvoiceId { get; set; }
        [Required] public decimal AppliedAmount { get; set; }
    }

    // --- SHIPMENTS ---
    public class PostShipmentDto
    {
        [Required] public string ConfirmedBy { get; set; } = "API User";
        [Required] public List<ShipmentLineDto> Lines { get; set; } = new();
    }

    public class ShipmentLineDto
    {
        [Required] public Guid ShipmentLineId { get; set; }
        [Required] public decimal QtyShipped { get; set; }
    }
}
