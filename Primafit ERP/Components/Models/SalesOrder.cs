using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum OrderStatus { Draft, Confirmed, Quote, Shipped, Invoiced, Cancelled }

    public class SalesOrder
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public string OrderNumber { get; set; } = string.Empty;

        // --- Tracks the original Quote Number ---
        public string? ConvertedFromQuoteNumber { get; set; }

        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }

        [Required]
        public Guid CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        public OrderStatus Status { get; set; } = OrderStatus.Draft;

        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ExchangeRate { get; set; } = 1;

        public Guid? ShipmentBatchId { get; set; }
        public Guid? InvoiceBatchId { get; set; }
        [Required]
        public Guid WarehouseId { get; set; }

        public List<SalesOrderLine> Lines { get; set; } = new();

        // --- UI COMPUTED HELPERS (Not saved to DB directly) ---
        [NotMapped] public decimal GrandTotalForeign { get; set; }
        [NotMapped] public decimal AmountPaid { get; set; }
        [NotMapped] public bool IsFullyPaid => Status == OrderStatus.Invoiced && AmountPaid >= GrandTotalForeign && GrandTotalForeign > 0;
    }

    public class SalesOrderLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid HeaderId { get; set; }
        [ForeignKey(nameof(HeaderId))]
        public SalesOrder? Header { get; set; }

        [Required]
        public Guid ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; } // Price in FOREIGN Currency

        // Computed helper
        [NotMapped]
        public decimal LineTotal => Quantity * UnitPrice;
    }
    public class SalesInvoice
    {
        public Guid Id { get; set; }
        public Guid SalesOrderId { get; set; }
        [Required]
        public Guid CompanyId { get; set; }
        public Guid CustomerId { get; set; }

        // FLEXIBILITY: User selects "Accounts Receivable" account manually
        // (e.g., user selects "1100 - Trade Debtors" or "1105 - Related Party Debtors")
        public Guid ReceivablesGlAccountId { get; set; }

        public List<SalesInvoiceLine> Lines { get; set; }
    }

    public class SalesInvoiceLine
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }

        // FLEXIBILITY: User selects "Sales Revenue" account manually
        // (e.g., "4000 - Product Sales" vs "4100 - Service Revenue")
        public Guid RevenueGlAccountId { get; set; }

        public decimal Amount { get; set; }

    }
}