using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum VendorReturnStatus
    {
        Draft = 0,
        Posted = 1,
        Void = 2
    }

    public enum VendorReturnType
    {
        QuantityOnly = 0,
        PaymentOnly = 1,
        Both = 2
    }

    public class VendorReturn
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid VendorId { get; set; }
        [ForeignKey(nameof(VendorId))]
        public virtual Vendor? Vendor { get; set; }

        public Guid? PurchaseOrderId { get; set; }
        [ForeignKey(nameof(PurchaseOrderId))]
        public virtual PurchaseOrder? PurchaseOrder { get; set; }

        public Guid? WarehouseId { get; set; }
        [ForeignKey(nameof(WarehouseId))]
        public virtual Warehouse? Warehouse { get; set; }

        public Guid? BankAccountId { get; set; }
        [ForeignKey(nameof(BankAccountId))]
        public virtual SegChartOfAccount? BankAccount { get; set; }

        [Required]
        [StringLength(50)]
        public string ReturnNumber { get; set; } = string.Empty;

        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DateTime TransactionDateTime { get; set; } = DateTime.Now;

        [StringLength(250)]
        public string Reason { get; set; } = string.Empty;

        public VendorReturnStatus Status { get; set; } = VendorReturnStatus.Draft;
        public VendorReturnType ReturnType { get; set; } = VendorReturnType.QuantityOnly;

        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1;

        [Column(TypeName = "decimal(18,4)")]
        public decimal TotalAmount { get; set; }

        public Guid? GlBatchId { get; set; }

        // --- CUSTOM TRANSACTION TEMPLATE & GL ROUTING OVERRIDES ---
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public virtual CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideGrIrClearingGlAccountId { get; set; }
        public Guid? OverrideInventoryAssetGlAccountId { get; set; }
        public Guid? OverrideAccountsPayableGlAccountId { get; set; }

        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? PostedByUserId { get; set; }
        public DateTime? PostedAt { get; set; }

        public List<VendorReturnLine> Lines { get; set; } = new();
    }

    public class VendorReturnLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid HeaderId { get; set; }
        [ForeignKey(nameof(HeaderId))]
        public virtual VendorReturn? Header { get; set; }

        public Guid PurchaseOrderLineId { get; set; }

        public Guid ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public virtual Item? Item { get; set; }

        public Guid? UomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        [Column(TypeName = "decimal(18,4)")]
        public decimal UomConversionFactor { get; set; } = 1m;

        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitCost { get; set; }

        // Workspace runtime tracking variables
        [NotMapped] public decimal OriginalReceivedQty { get; set; }
        [NotMapped] public decimal PreviouslyReturnedQty { get; set; }
        [NotMapped] public decimal MaxReturnableQty { get; set; }
        [NotMapped] public decimal LineTotal => Quantity * UnitCost;
    }
}
