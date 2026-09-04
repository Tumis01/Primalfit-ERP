using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // =================================────────────────================
    // 1. THE CORE DOCUMENT ENGINE (Handles Requests, POs, and Invoices)
    // =================================────────────────================
    public class PurchaseOrder
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }

        [Required]
        public Guid VendorId { get; set; }
        public string OrderNumber { get; set; } = string.Empty; // Holds REQ-..., PO-..., or INV-...
        public DateTime OrderDate { get; set; } = DateTime.Today;
        public Guid? LinkedSalesOrderId { get; set; }

        [Required]
        public Guid CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1;

        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Open;
        public string? ConvertedFromRequestNumber { get; set; }
        public string? ConvertedFromPONumber { get; set; } 
        public bool IsDirectInvoice { get; set; } = false;  
        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; } 

        [Column(TypeName = "decimal(18,4)")]
        public decimal DiscountPercentage { get; set; } = 0;

        [Column(TypeName = "decimal(18,4)")]
        public decimal DiscountAmount { get; set; } = 0;

        public Guid? DiscountGlAccountId { get; set; }
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? AccountsPayableGlAccountId { get; set; }
        public Guid? GoodsReceiptClearingGlAccountId { get; set; }
        public Guid? GLBatchId { get; set; }

        public List<PurchaseOrderLine> Lines { get; set; } = new();
        public bool HasReceipt { get; set; } = false;
        public bool IsFullyReceived { get; set; } = false;
        public bool IsInvoicePosted { get; set; } = false;
        public bool IsFullyPaid { get; set; } = false;

        [NotMapped] public decimal GrandTotalForeign { get; set; }
    }

    public class PurchaseOrderLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PurchaseOrderId { get; set; }
        public Guid ItemId { get; set; }
        public decimal QuantityOrdered { get; set; }
        public decimal UnitCost { get; set; } 
        public decimal QuantityReceived { get; set; } = 0; 
        public decimal QuantityBilled { get; set; } = 0;  
    }

    // =================================────────────────================
    // 2. THE PHYSICAL LOGISTICS RECEIPT (GRN - Managed inside Invoice View)
    // =================================────────────────================
    public class GoodsReceipt
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid PurchaseOrderId { get; set; }
        public string GrnNumber { get; set; } = string.Empty;
        public DateTime DateReceived { get; set; } = DateTime.Today;
        public Guid InventoryGlAccountId { get; set; }
        public Guid? GLBatchId { get; set; }
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public virtual CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideInventoryAssetGlAccountId { get; set; }
        public Guid? OverrideGrIrClearingGlAccountId { get; set; }
        public List<GoodsReceiptLine> Lines { get; set; } = new();
    }

    public class GoodsReceiptLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GoodsReceiptId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public decimal QuantityReceived { get; set; }
        [NotMapped] public decimal MaxAllowed { get; set; }
    }

    // =================================────────────────================
    // 3. THE FINANCIAL LEDGER LIABILITIES (Unchanged for Backwards Compatibility)
    // =================================================================
    public class VendorBill
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

        public Guid AccountsPayableGlId { get; set; }
        public bool IsDirectBill { get; set; } = false;
        public string ExternalInvoiceNumber { get; set; } = string.Empty;
        public DateTime BillDate { get; set; } = DateTime.Today;
        public Guid CurrencyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public virtual Currency? Currency { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal ExchangeRate { get; set; } = 1;

        [Column(TypeName = "decimal(18,4)")]
        public decimal TotalAmountForeign { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal TotalAmount { get; set; }

        public BillMatchStatus MatchStatus { get; set; } = BillMatchStatus.Pending;
        public string MatchVarianceReason { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }

        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public virtual CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideExpenseGlAccountId { get; set; }
        public Guid? OverrideAccountsPayableGlAccountId { get; set; }

        public List<VendorBillLine> Lines { get; set; } = new();
        public bool IsPosted { get; set; } = false;
        public DateTime? PostedDate { get; set; }
        public Guid? GLBatchId { get; set; }
        public virtual List<VendorPayment> Payments { get; set; } = new();

        [NotMapped] public decimal AmountPaid => Payments?.Sum(p => p.Amount) ?? 0;
        [NotMapped] public decimal BalanceDue => TotalAmount - AmountPaid;
        [NotMapped] public bool IsFullyPaid => IsPosted && TotalAmount > 0 && BalanceDue <= 0.01m;
        [NotMapped]
        public string CalculatedPaymentStatus
        {
            get
            {
                decimal totalPaid = Payments?.Sum(p => p.Amount) ?? 0;
                if (totalPaid <= 0) return "Unpaid";
                if (totalPaid >= TotalAmount - 0.01m) return "Paid in Full";
                return "Partially Paid";
            }
        }
    }

    public class VendorBillLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid VendorBillId { get; set; }
        [ForeignKey(nameof(VendorBillId))]
        public virtual VendorBill? VendorBill { get; set; }

        public Guid? ItemId { get; set; }
        [ForeignKey(nameof(ItemId))]
        public virtual Item? Item { get; set; }

        public Guid ExpenseGlAccountId { get; set; }
        public string? Description { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal QuantityBilled { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitCostBilled { get; set; }

        [NotMapped]
        public decimal LineTotal => QuantityBilled * UnitCostBilled;
    }

    // =================================================================
    // 2. AP DISBURSEMENTS (Vendor Payments)
    // =================================================================
    public class VendorPayment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid VendorBillId { get; set; }
        [ForeignKey(nameof(VendorBillId))]
        public virtual VendorBill? VendorBill { get; set; }

        public DateTime Date { get; set; } = DateTime.Today;

        [Column(TypeName = "decimal(18,4)")]
        public decimal Amount { get; set; }

        public Guid BankGlAccountId { get; set; }
        public string Reference { get; set; } = string.Empty;

        // --- CUSTOM TRANSACTION TEMPLATE & GL OVERRIDES ---
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey(nameof(CustomTransactionTypeId))]
        public virtual CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideDebitApGlAccountId { get; set; }
        public Guid? OverrideCreditBankGlAccountId { get; set; }
        public Guid? GLBatchId { get; set; }
    }

    public class ItemCostHistory
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; }
        public DateTime DateChanged { get; set; } = DateTime.UtcNow;
        [Column(TypeName = "decimal(18,4)")] public decimal OldQty { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal OldWacc { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal NewQtyIn { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal NewCostIn { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal ResultingWacc { get; set; }
        public string Reference { get; set; } = string.Empty;
    }

    public class WaccHistory
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; }
        public DateTime DateChanged { get; set; } = DateTime.UtcNow;
        public string Reference { get; set; } = "";
        [Column(TypeName = "decimal(18,4)")] public decimal OldQty { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal OldWacc { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal IncomingQty { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal IncomingCost { get; set; }
        [Column(TypeName = "decimal(18,4)")] public decimal NewWacc { get; set; }
    }

    // =================================================================
    // 4. WORKFLOW LIFECYCLE ENUMS
    // =================================────────────────================
    public enum PurchaseOrderStatus
    {
        Open = 0,               
        Request = 1,            
        PartiallyReceived = 2,  
        DraftInvoice = 3,       
        Invoiced = 4,           
        Closed = 5,             
        Cancelled = 6          
    }

    public enum BillMatchStatus
    {
        Pending,
        Matched,
        Variance,
        NoPoLinked
    }

}
