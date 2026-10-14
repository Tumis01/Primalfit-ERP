using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models;

public enum NumberingDefaultsModule
{
    AccountsReceivable,
    AccountsPayable,
    Inventory
}

public enum SequenceTargetType
{
    AR_Quote,
    AR_Order,
    AR_Invoice,
    AR_CreditNote,
    AR_ReceiptRefund,
    AR_Shipment,
    AR_CustomerReceipt,
    AR_Adjustment,
    AP_Request,
    AP_PurchaseOrder,
    AP_Invoice,
    AP_DebitNote,
    AP_ReturnToVendor,
    AP_GoodsReceipt,
    AP_VendorPayment,
    AP_Adjustment,
    CustomerCode,
    VendorCode,
    ItemCategoryCode,
    ItemCode
}

public enum ResetInterval
{
    Never,
    Monthly,
    Yearly,
    Periodically,
    FiscalYearly
}

public enum SequenceComponentType
{
    Constant,
    SequenceNumber,
    Year,
    Month,
    Day,
    FiscalYear
}

public class SequenceDefinition
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CompanyId { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public SequenceTargetType TargetType { get; set; }
    public ResetInterval ResetInterval { get; set; } = ResetInterval.Never;
    public bool IsSystemDefault { get; set; }
    public bool IsActive { get; set; }

    public List<SequenceComponent> Components { get; set; } = new();
}

public class SequenceComponent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SequenceDefinitionId { get; set; }

    public int OrderIndex { get; set; }
    public SequenceComponentType ComponentType { get; set; }
    public int Length { get; set; } = 1;

    [StringLength(20)]
    public string? Formula { get; set; }

    public SequenceDefinition? SequenceDefinition { get; set; }
}

public class SequenceTracker
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SequenceDefinitionId { get; set; }

    [Required, StringLength(24)]
    public string ScopePeriodKey { get; set; } = string.Empty;

    public long LastNumber { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public SequenceDefinition? SequenceDefinition { get; set; }
}
