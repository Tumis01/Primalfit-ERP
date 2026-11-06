using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models;

public enum SalesQuoteStatus
{
    Draft,
    Open,
    PartiallyConverted,
    Converted,
    Cancelled,
    Expired
}

/// <summary>A sales quotation, stored independently from a sales order.</summary>
public class SalesQuote
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

    // Warehouse is the application's site dimension.
    public Guid? WarehouseId { get; set; }
    [ForeignKey(nameof(WarehouseId))]
    public Warehouse? Warehouse { get; set; }

    [Required]
    public Guid CurrencyId { get; set; }
    [ForeignKey(nameof(CurrencyId))]
    public Currency? Currency { get; set; }

    [Required]
    public string QuoteNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateTime TransactionDateTime { get; set; } = DateTime.Now;
    public SalesQuoteStatus Status { get; set; } = SalesQuoteStatus.Open;
    public DateOnly? ValidUntil { get; set; }

    public Guid? ProjectId { get; set; }
    [ForeignKey(nameof(ProjectId))]
    public Project? Project { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ExchangeRate { get; set; } = 1m;
    [Column(TypeName = "decimal(18,4)")]
    public decimal DiscountPercentage { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal DiscountAmount { get; set; }
    public Guid? DiscountGlAccountId { get; set; }
    public Guid? TaxId { get; set; }
    public Guid? TaxGlAccountId { get; set; }
    public Guid? WithholdingTaxId { get; set; }
    public string? WithholdingTaxName { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal WithholdingPercentage { get; set; }
    public Guid? WithholdingSalesGlAccountId { get; set; }

    public List<SalesQuoteLine> Lines { get; set; } = new();
}

public class SalesQuoteLine : IDimensionedLine
{
        public Guid? DimensionSetId { get; set; }

    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SalesQuoteId { get; set; }
    [ForeignKey(nameof(SalesQuoteId))]
    public SalesQuote? SalesQuote { get; set; }

    public Guid? ItemId { get; set; }
    [ForeignKey(nameof(ItemId))]
    public Item? Item { get; set; }
    public string? Description { get; set; }
    public Guid? UomId { get; set; }
    public string UomName { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")]
    public decimal UomConversionFactor { get; set; } = 1m;
    [Column(TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal UnitPrice { get; set; }
    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice;
}
