using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum CreditNoteStatus { Draft, Approved, Posted, Void }

    public class CreditNote
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public string CreditNoteNumber { get; set; } = string.Empty;

        [Required]
        public Guid CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        // --- CHANGE: Link to Order instead of Invoice ---
        [Required]
        public Guid SalesOrderId { get; set; }
        [ForeignKey(nameof(SalesOrderId))]
        public SalesOrder? SalesOrder { get; set; }

        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DateTime TransactionDateTime { get; set; } = DateTime.Now;

        [Required]
        public string Reason { get; set; } = string.Empty;

        public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Draft;

        [Required]
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1;

        [Column(TypeName = "decimal(18,4)")]
        public decimal TotalAmount { get; set; }

        public bool ReturnToStock { get; set; } = false;

        public Guid? WarehouseId { get; set; }
        [ForeignKey(nameof(WarehouseId))]
        public Warehouse? Warehouse { get; set; }

        public Guid? GlBatchId { get; set; }

        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? PostedByUserId { get; set; }
        public DateTime? PostedAt { get; set; }
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideRevenueGlAccountId { get; set; }
        public Guid? OverrideReceivablesGlAccountId { get; set; }
        public List<CreditNoteLine> Lines { get; set; } = new();
    }

    public class CreditNoteLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid HeaderId { get; set; }
        [ForeignKey(nameof(HeaderId))]
        public CreditNote? Header { get; set; }

        [Required]
        public Guid ItemId { get; set; }

        public Guid? UomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        [Column(TypeName = "decimal(18,4)")]
        public decimal UomConversionFactor { get; set; } = 1m;
        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        // Link to the specific Order Line being returned
        public Guid SalesOrderLineId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitPrice { get; set; }

        [NotMapped]
        public decimal LineTotal => Quantity * UnitPrice;

        // --- NEW: UI COMPUTED HELPERS ---
        [NotMapped]
        public decimal OriginalSoldQty { get; set; }

        [NotMapped]
        public decimal MaxReturnableQty { get; set; }
    }
}
