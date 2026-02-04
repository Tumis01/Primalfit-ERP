using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum OrderStatus { Draft, Confirmed, Shipped, Invoiced, Cancelled }

    public class SalesOrder
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; } // Strict Multi-tenancy

        [Required]
        public string OrderNumber { get; set; } = string.Empty; // e.g. SO-2026-0001

        [Required]
        public Guid CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public BusinessPartner? Customer { get; set; }

        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        public OrderStatus Status { get; set; } = OrderStatus.Draft;

        // --- FINANCIALS ---
        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ExchangeRate { get; set; } = 1; // Locked at creation

        // --- LINKS ---
        public Guid? ShipmentBatchId { get; set; } // Link to GL Batch
        public Guid? InvoiceBatchId { get; set; }  // Link to GL Batch

        public List<SalesOrderLine> Lines { get; set; } = new();
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
}