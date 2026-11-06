using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum OrderStatus { Draft, Order, Confirmed, Quote, PartiallyShipped, Shipped, PartiallyInvoiced, Invoiced, Cancelled }

    public class SalesOrder
    {
        public Guid? DimensionSetId { get; set; }

        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        public Guid? ProjectId { get; set; }
        [ForeignKey(nameof(ProjectId))]
        public Project? Project { get; set; }

        public Guid WarehouseId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        // Accounting still posts by Date, while this timestamp records the
        // exact business transaction time entered by the user.
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DateTime TransactionDateTime { get; set; } = DateTime.Now;

        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1;

        public OrderStatus Status { get; set; } = OrderStatus.Draft;
        public bool IsDirectInvoice { get; set; } = false;

        public string? ConvertedFromQuoteNumber { get; set; }

        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }

        // Sales withholding is configured on quotes/orders and carried into
        // the invoice; the receivable is recognized when a payment is posted.
        public Guid? WithholdingTaxId { get; set; }
        public string? WithholdingTaxName { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal WithholdingPercentage { get; set; }
        public Guid? WithholdingSalesGlAccountId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal DiscountPercentage { get; set; } = 0;

        [Column(TypeName = "decimal(18,4)")]
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

        // Number formats are configurable, so document type must come from the
        // workflow fields rather than the text of OrderNumber. The prefix check
        // keeps older invoices recognizable when they predate these markers.
        [NotMapped]
        public bool IsInvoiceDocument => IsDirectInvoice
            || InvoiceBatchId.HasValue
            || (ConvertedFromQuoteNumber != null && Status == OrderStatus.Draft)
            || OrderNumber.StartsWith("INV", StringComparison.OrdinalIgnoreCase);

        [NotMapped] public decimal NetInvoiceTotalForeign => Math.Max(0, GrandTotalForeign - CreditNoteTotal);
        [NotMapped] public decimal BalanceDueForeign => Math.Max(0, NetInvoiceTotalForeign - AmountPaid);
        [NotMapped] public bool IsFullyPaid => BalanceDueForeign <= 0.01m && Status == OrderStatus.Invoiced;
    }

    public class SalesOrderLine : IDimensionedLine
    {
        public Guid? DimensionSetId { get; set; }

        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid HeaderId { get; set; }
        [ForeignKey(nameof(HeaderId))]
        public virtual SalesOrder? Header { get; set; }

        public Guid? SalesQuoteLineId { get; set; }
        [ForeignKey(nameof(SalesQuoteLineId))]
        public SalesQuoteLine? SalesQuoteLine { get; set; }

        public Guid? ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public virtual Item? Item { get; set; }

        public string? Description { get; set; }

        public Guid? UomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        [Column(TypeName = "decimal(18,4)")]
        public decimal UomConversionFactor { get; set; } = 1m;

        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal QtyShipped { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal QtyInvoiced { get; set; }

        [NotMapped] public decimal QtyCredited { get; set; }
        [NotMapped]
        public decimal QtyReturned { get; set; }
        [NotMapped] public decimal LineTotal => Quantity * UnitPrice;
    }
    public enum SalesInvoiceStatus { Draft, InReview, Posted, Cancelled }

    public class SalesInvoice
    {
        public Guid? DimensionSetId { get; set; }

        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        // Legacy single-order header link. New source links are line-level.
        public Guid? SalesOrderId { get; set; }
        public Guid? WarehouseId { get; set; }
        [ForeignKey(nameof(WarehouseId))]
        public Warehouse? Warehouse { get; set; }
        public Guid? ProjectId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DateTime TransactionDateTime { get; set; } = DateTime.Now;
        public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Draft;
        public bool IsDirectInvoice { get; set; }

        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1m;

        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal DiscountPercentage { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal DiscountAmount { get; set; }
        public Guid? DiscountGlAccountId { get; set; }
        public Guid? WithholdingTaxId { get; set; }
        public string? WithholdingTaxName { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal WithholdingPercentage { get; set; }
        public Guid? WithholdingSalesGlAccountId { get; set; }

        public Guid? ReceivablesGlAccountId { get; set; }
        public Guid? DirectIncomeGlAccountId { get; set; }
        public Guid? CustomTransactionTypeId { get; set; }
        public Guid? InvoiceBatchId { get; set; }
        public bool IsPaid { get; set; }
        public decimal TotalAmountForeign { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime? PostedDate { get; set; }
        public Guid? GLBatchId { get; set; }
        public List<SalesInvoiceLine> Lines { get; set; } = new();

        [NotMapped] public decimal AmountPaid { get; set; }
        [NotMapped] public decimal CreditNoteTotal { get; set; }
        [NotMapped] public decimal GrandTotalForeign { get; set; }
        [NotMapped] public decimal BalanceDueForeign => Math.Max(0, GrandTotalForeign - CreditNoteTotal - AmountPaid);
    }

    public class SalesInvoiceLine : IDimensionedLine
    {
        public Guid? DimensionSetId { get; set; }

        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? SalesInvoiceId { get; set; }
        [ForeignKey(nameof(SalesInvoiceId))]
        public SalesInvoice? SalesInvoice { get; set; }
        public Guid? ShipmentLineId { get; set; }
        [ForeignKey(nameof(ShipmentLineId))]
        public SalesShipmentLine? ShipmentLine { get; set; }
        public Guid? SalesOrderLineId { get; set; }
        [ForeignKey(nameof(SalesOrderLineId))]
        public SalesOrderLine? SalesOrderLine { get; set; }
        public Guid? ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }
        [Column("Amount", TypeName = "decimal(18,4)")]
        public decimal? LegacyAmount { get; set; }
        public Guid? RevenueGlAccountId { get; set; }
        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal DiscountAmount { get; set; }
        public string? Description { get; set; }
        public Guid? UomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        [Column(TypeName = "decimal(18,4)")]
        public decimal UomConversionFactor { get; set; } = 1m;
        [Column(TypeName = "decimal(18,4)")]
        public decimal QuantityInvoiced { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitPrice { get; set; }
        [NotMapped]
        public decimal Amount => Math.Round(QuantityInvoiced * UnitPrice, 2, MidpointRounding.AwayFromZero);
    }
    public enum ShipmentStatus { Pending, PartiallyShipped, Shipped, Cancelled }

    public class SalesShipment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required]
        public Guid CompanyId { get; set; }
        // Legacy single-order link; line source links support multi-order shipments.
        public Guid? SalesOrderId { get; set; }
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

    public class SalesShipmentLine : IDimensionedLine
    {
        public Guid? DimensionSetId { get; set; }

        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required]
        public Guid ShipmentId { get; set; }
        [ForeignKey(nameof(ShipmentId))]
        public SalesShipment? Shipment { get; set; }

        public Guid? SalesOrderLineId { get; set; }
        [ForeignKey(nameof(SalesOrderLineId))]
        public SalesOrderLine? SalesOrderLine { get; set; }
        public Guid? SalesInvoiceLineId { get; set; }
        [ForeignKey(nameof(SalesInvoiceLineId))]
        public SalesInvoiceLine? SalesInvoiceLine { get; set; }
        public Guid ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        public Guid? UomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        [Column(TypeName = "decimal(18,4)")]
        public decimal UomConversionFactor { get; set; } = 1m;

        [Column(TypeName = "decimal(18,4)")]
        public decimal QtyOrdered { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal QtyShipped { get; set; }
    }
}
