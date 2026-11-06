using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models;

public enum PurchaseRequestStatus
{
    Draft,
    Open,
    PartiallyConverted,
    Converted,
    Cancelled
}

/// <summary>A purchasing request, stored independently from a purchase order.</summary>
public class PurchaseRequest
{
        public Guid? DimensionSetId { get; set; }

    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    public Guid VendorId { get; set; }
    [ForeignKey(nameof(VendorId))]
    public Vendor? Vendor { get; set; }

    // Warehouse is the application's site dimension.
    public Guid? WarehouseId { get; set; }
    [ForeignKey(nameof(WarehouseId))]
    public Warehouse? Warehouse { get; set; }

    [Required]
    public Guid CurrencyId { get; set; }
    [ForeignKey(nameof(CurrencyId))]
    public Currency? Currency { get; set; }

    [Required]
    public string RequestNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; } = DateTime.Now;
    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Open;

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
    [Column(TypeName = "decimal(18,4)")]
    public decimal WithholdingAmountForeign { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal WithholdingAmount { get; set; }
    public Guid? WithholdingGlAccountId { get; set; }

    public List<PurchaseRequestLine> Lines { get; set; } = new();
}

public class PurchaseRequestLine : IDimensionedLine
{
        public Guid? DimensionSetId { get; set; }

    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PurchaseRequestId { get; set; }
    [ForeignKey(nameof(PurchaseRequestId))]
    public PurchaseRequest? PurchaseRequest { get; set; }

    [Required]
    public Guid ItemId { get; set; }
    [ForeignKey(nameof(ItemId))]
    public Item? Item { get; set; }
    public Guid? UomId { get; set; }
    public string UomName { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")]
    public decimal UomConversionFactor { get; set; } = 1m;
    [Column(TypeName = "decimal(18,4)")]
    public decimal QuantityRequested { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal UnitCost { get; set; }
    public string? Description { get; set; }
}
