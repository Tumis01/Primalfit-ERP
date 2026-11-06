using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models;

// Drafts reserve order quantities but are not physical receipts or GL entries.
public class GoodsReceiptDraft
{
        public Guid? DimensionSetId { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid VendorId { get; set; }
    public Guid WarehouseId { get; set; }
    [MaxLength(200)] public string GrnNumber { get; set; } = string.Empty;
    public DateTime DateReceived { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid InventoryGlAccountId { get; set; }
    public Guid? CustomTransactionTypeId { get; set; }
    public Guid? OverrideInventoryAssetGlAccountId { get; set; }
    public Guid? OverrideGrIrClearingGlAccountId { get; set; }
    public List<GoodsReceiptDraftLine> Lines { get; set; } = new();
}

public class GoodsReceiptDraftLine : IDimensionedLine
{
        public Guid? DimensionSetId { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoodsReceiptDraftId { get; set; }
    public GoodsReceiptDraft? GoodsReceiptDraft { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public Guid? PurchaseInvoiceLineId { get; set; }
    public PurchaseInvoiceLine? PurchaseInvoiceLine { get; set; }
    public Guid? UomId { get; set; }
    [MaxLength(200)] public string UomName { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")] public decimal UomConversionFactor { get; set; } = 1m;
    [Column(TypeName = "decimal(18,4)")] public decimal QuantityReceived { get; set; }
}
