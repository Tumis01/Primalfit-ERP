using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum DebitNoteStatus
    {
        Draft = 0,
        Posted = 1,
        Void = 2
    }

    public class DebitNote
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid VendorId { get; set; }
        [ForeignKey(nameof(VendorId))]
        public Vendor? Vendor { get; set; }

        public Guid? PurchaseOrderId { get; set; }
        [ForeignKey(nameof(PurchaseOrderId))]
        public PurchaseOrder? PurchaseOrder { get; set; }

        [Required]
        public string DebitNoteNumber { get; set; } = string.Empty;

        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DebitNoteStatus Status { get; set; } = DebitNoteStatus.Draft;
        public string Reason { get; set; } = string.Empty;

        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1;

        [Column(TypeName = "decimal(18,4)")]
        public decimal TotalAmount { get; set; }

        public bool ReturnToStock { get; set; } = true;
        public Guid? WarehouseId { get; set; }
        [ForeignKey(nameof(WarehouseId))]
        public Warehouse? Warehouse { get; set; }

        public Guid? GlBatchId { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? PostedByUserId { get; set; }
        public DateTime? PostedAt { get; set; }
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public virtual CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideAccountsPayableGlAccountId { get; set; }
        public Guid? OverrideGrIrClearingGlAccountId { get; set; }

        public List<DebitNoteLine> Lines { get; set; } = new();
    }

    public class DebitNoteLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid HeaderId { get; set; }
        [ForeignKey(nameof(HeaderId))]
        public DebitNote? Header { get; set; }

        public Guid PurchaseOrderLineId { get; set; }

        public Guid ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitCost { get; set; }

        [NotMapped]
        public decimal OriginalPurchasedQty { get; set; }

        [NotMapped]
        public decimal MaxReturnableQty { get; set; }

        [NotMapped]
        public decimal LineTotal => Quantity * UnitCost;
    }
}