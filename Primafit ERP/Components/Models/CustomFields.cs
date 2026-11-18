using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models;

public enum CustomFieldScope
{
    SalesOrderHeader, SalesOrderLine, Customer, Vendor, Item,
    JournalHeader, JournalLine, CashbookHeader, CashbookLine,
    SalesQuoteHeader, SalesQuoteLine, SalesInvoiceHeader, SalesInvoiceLine,
    PurchaseRequestHeader, PurchaseRequestLine, PurchaseOrderHeader, PurchaseOrderLine,
    PurchaseInvoiceHeader, PurchaseInvoiceLine, GoodsReceiptHeader, GoodsReceiptLine,
    ShipmentHeader, ShipmentLine, StockTransferHeader, StockTransferLine,
    CreditNoteHeader, CreditNoteLine, DebitNoteHeader, DebitNoteLine,
    ReceiptRefundHeader, ReceiptRefundLine, VendorReturnHeader, VendorReturnLine,
    CustomerPayment, VendorPayment, InventoryAdjustmentHeader, InventoryAdjustmentLine,
    GeneralLedgerAdjustmentHeader, GeneralLedgerAdjustmentLine, CustomerAdjustmentHeader, CustomerAdjustmentLine, VendorAdjustmentHeader, VendorAdjustmentLine,
    StockEntryHeader, StockEntryLine, BankReconciliationHeader, BankReconciliationLine
}
// Keep the stored IDs of non-text types unchanged when merging the former text types.
public enum CustomFieldDataType { Text = 0, Decimal = 2, Date = 3, Boolean = 4, SingleSelect = 5 }
public enum SalesOrderSubmissionState { Submitted, Draft }

public sealed class CustomFieldDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public CustomFieldScope Scope { get; set; }
    [MaxLength(60)] public string Code { get; set; } = "";
    public CustomFieldDataType DataType { get; set; }
    public bool IsActive { get; set; } = true;
    public int CurrentRevision { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public List<CustomFieldRevision> Revisions { get; set; } = new();
    public List<CustomFieldOption> Options { get; set; } = new();
}

public sealed class CustomFieldRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid DefinitionId { get; set; }
    public int RevisionNumber { get; set; }
    [Required, MaxLength(100)] public string FieldName { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool Required { get; set; }
    [Range(1, 4000)] public int MaxLength { get; set; } = 200;
    [Column(TypeName = "decimal(18,4)")] public decimal? Minimum { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Maximum { get; set; }
    public int DecimalPlaces { get; set; } = 4;
    public bool IncludeOnPrint { get; set; }
    public bool IncludeInExports { get; set; } = true;
    public bool IsReportable { get; set; } = true;
    public bool IsFilterable { get; set; } = true;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public string PublishedByUserId { get; set; } = "";
}

public sealed class CustomFieldOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid DefinitionId { get; set; }
    [MaxLength(100)] public string Label { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

[NotMapped]
public abstract class CustomFieldValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid RevisionId { get; set; }
    [MaxLength(4000)] public string? TextValue { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? DecimalValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BooleanValue { get; set; }
    public Guid? OptionId { get; set; }
    [MaxLength(100)] public string? OptionLabelSnapshot { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedByUserId { get; set; } = "";
    [NotMapped] public abstract Guid OwnerId { get; set; }
}

public sealed class SalesOrderCustomFieldValue : CustomFieldValue
{
    public Guid SalesOrderId { get; set; }
    public override Guid OwnerId { get => SalesOrderId; set => SalesOrderId = value; }
}
public sealed class SalesOrderLineCustomFieldValue : CustomFieldValue
{
    public Guid SalesOrderId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public override Guid OwnerId { get => SalesOrderLineId; set => SalesOrderLineId = value; }
}
public sealed class CustomerCustomFieldValue : CustomFieldValue
{
    public Guid CustomerId { get; set; }
    public override Guid OwnerId { get => CustomerId; set => CustomerId = value; }
}
public sealed class VendorCustomFieldValue : CustomFieldValue
{
    public Guid VendorId { get; set; }
    public override Guid OwnerId { get => VendorId; set => VendorId = value; }
}
public sealed class ItemCustomFieldValue : CustomFieldValue
{
    public Guid ItemId { get; set; }
    public override Guid OwnerId { get => ItemId; set => ItemId = value; }
}

public sealed class TransactionCustomFieldValue<TOwner> : CustomFieldValue where TOwner : class
{
    public Guid RecordId { get; set; }
    public Guid? ParentId { get; set; }
    public override Guid OwnerId { get => RecordId; set => RecordId = value; }
}

public sealed class InventoryAdjustmentDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid GLBatchId { get; set; }
    public string Reference { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateTime? CommittedAt { get; set; }
    [ConcurrencyCheck] public Guid? CustomFieldsVersion { get; set; }
    public List<InventoryAdjustmentRecordLine> Lines { get; set; } = new();
}
public sealed class InventoryAdjustmentRecordLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HeaderId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal QuantityChange { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal ValueChange { get; set; }
    [ConcurrencyCheck] public Guid? CustomFieldsVersion { get; set; }
}

public sealed class InventoryEntryDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid? GLBatchId { get; set; }
    public string Reference { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateTime? CommittedAt { get; set; }
    [ConcurrencyCheck] public Guid? CustomFieldsVersion { get; set; }
    public List<InventoryEntryRecordLine> Lines { get; set; } = new();
}
public sealed class InventoryEntryRecordLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HeaderId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal QuantityChange { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal ValueChange { get; set; }
    [ConcurrencyCheck] public Guid? CustomFieldsVersion { get; set; }
}

public sealed class CustomFieldInput
{
    public Guid DefinitionId { get; set; }
    public string? TextValue { get; set; }
    public decimal? DecimalValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BooleanValue { get; set; }
    public Guid? OptionId { get; set; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(TextValue) && !DecimalValue.HasValue && !DateValue.HasValue && !BooleanValue.HasValue && !OptionId.HasValue;
    public static CustomFieldInput From(CustomFieldValue v) => new() { DefinitionId = v.DefinitionId, TextValue = v.TextValue, DecimalValue = v.DecimalValue, DateValue = v.DateValue, BooleanValue = v.BooleanValue, OptionId = v.OptionId };
}
public sealed class CustomFieldEditorField
{
    public CustomFieldDefinition Definition { get; set; } = new();
    public CustomFieldRevision Revision { get; set; } = new();
    public CustomFieldInput Input { get; set; } = new();
    public List<CustomFieldOption> Options { get; set; } = new();
    public string? Error { get; set; }
    public string? ParseError { get; set; }
}
public sealed class CustomFieldFormState
{
    public Guid CompanyId { get; set; }
    public CustomFieldScope Scope { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ExpectedOwnerVersion { get; set; }
    public string SchemaToken { get; set; } = "";
    public bool CanEdit { get; set; }
    public List<CustomFieldEditorField> Fields { get; set; } = new();
    public string Error { get; set; } = "";
}
public sealed class CustomFieldDefinitionEdit
{
    public CustomFieldDefinition Definition { get; set; } = new();
    public CustomFieldRevision Revision { get; set; } = new();
    public List<CustomFieldOption> Options { get; set; } = new();
}
