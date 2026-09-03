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
        public Guid CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        public Guid WarehouseId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ExchangeRate { get; set; } = 1;

        public OrderStatus Status { get; set; } = OrderStatus.Draft;
        public bool IsDirectInvoice { get; set; } = false;

        public string? ConvertedFromQuoteNumber { get; set; }

        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountPercentage { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; } = 0;

        public Guid? DiscountGlAccountId { get; set; }

        // --- CUSTOM TRANSACTION TEMPLATE & GL OVERRIDES ---
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public virtual CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? ReceivablesGlAccountId { get; set; }
        public Guid? DirectIncomeGlAccountId { get; set; }

        public Guid? InvoiceBatchId { get; set; }
        public Guid? ShipmentBatchId { get; set; }

        public virtual List<SalesOrderLine> Lines { get; set; } = new();

        // --- PARTIAL PAYMENT & REMAINING CEILING PROPERTIES ---
        [NotMapped] public decimal AmountPaid { get; set; }
        [NotMapped] public decimal CreditNoteTotal { get; set; }
        [NotMapped] public decimal GrandTotalForeign { get; set; }

        [NotMapped] public decimal NetInvoiceTotalForeign => Math.Max(0, GrandTotalForeign - CreditNoteTotal);
        [NotMapped] public decimal BalanceDueForeign => Math.Max(0, NetInvoiceTotalForeign - AmountPaid);
        [NotMapped] public bool IsFullyPaid => BalanceDueForeign <= 0.01m && Status == OrderStatus.Invoiced;
    }

    public class SalesOrderLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid HeaderId { get; set; }
        [ForeignKey(nameof(HeaderId))]
        public virtual SalesOrder? Header { get; set; }

        public Guid? ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public virtual Item? Item { get; set; }

        public string? Description { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyShipped { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyInvoiced { get; set; }

        [NotMapped] public decimal QtyCredited { get; set; }
        [NotMapped]
        public decimal QtyReturned { get; set; }
        [NotMapped] public decimal LineTotal => Quantity * UnitPrice;
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
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideCogsGlAccountId { get; set; }
        public Guid? OverrideInventoryAssetGlAccountId { get; set; }

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