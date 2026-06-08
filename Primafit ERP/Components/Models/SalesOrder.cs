using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum OrderStatus { Draft, Order, Confirmed, Quote, PartiallyShipped, Shipped, PartiallyInvoiced, Invoiced, Cancelled }

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
        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountPercentage { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; } = 0;

        public Guid? DiscountGlAccountId { get; set; }
        public bool IsDirectInvoice { get; set; } = false;
        public Guid? DirectIncomeGlAccountId { get; set; } 
        public List<SalesOrderLine> Lines { get; set; } = new();

        [NotMapped] public bool IsFullyPaid => Status == OrderStatus.Invoiced && GrandTotalForeign > 0 && AmountPaid >= (GrandTotalForeign - 0.01m);
        [NotMapped] public decimal GrandTotalForeign { get; set; }
        [NotMapped] public decimal AmountPaid { get; set; }
        [NotMapped] public decimal BalanceDue => GrandTotalForeign - AmountPaid;
        [NotMapped]
        public decimal CreditNoteTotal { get; set; }
    }

    public class SalesOrderLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid HeaderId { get; set; }
        [ForeignKey(nameof(HeaderId))]
        public SalesOrder? Header { get; set; }
        public Guid? ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }         
        [NotMapped]
        public decimal LineTotal => Quantity * UnitPrice;
        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyShipped { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyInvoiced { get; set; } = 0;
        public string? Description { get; set; }
        [NotMapped]
        public decimal QtyCredited { get; set; }
    }
    public class SalesInvoice
    {
        public Guid Id { get; set; }
        public Guid SalesOrderId { get; set; }
        [Required]
        public Guid CompanyId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid ReceivablesGlAccountId { get; set; }

        public List<SalesInvoiceLine> Lines { get; set; }
    }

    public class SalesInvoiceLine
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public Guid RevenueGlAccountId { get; set; }

        public decimal Amount { get; set; }

    }
    public enum ShipmentStatus { Pending, PartiallyShipped, Shipped, Cancelled }

    public class SalesShipment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required]
        public Guid CompanyId { get; set; }
        [Required]
        public Guid SalesOrderId { get; set; }
        [ForeignKey(nameof(SalesOrderId))]
        public SalesOrder? SalesOrder { get; set; }

        public Guid WarehouseId { get; set; }
        public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime? ShippedDate { get; set; }
        public Guid? ShipmentBatchId { get; set; }

        public string ShipmentNumber { get; set; } = string.Empty;
        public string? ConfirmedBy { get; set; }
        public List<SalesShipmentLine> Lines { get; set; } = new();
    }

    public class SalesShipmentLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required]
        public Guid ShipmentId { get; set; }
        [ForeignKey(nameof(ShipmentId))]
        public SalesShipment? Shipment { get; set; }

        public Guid SalesOrderLineId { get; set; }
        public Guid ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyOrdered { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyShipped { get; set; }
    }
}