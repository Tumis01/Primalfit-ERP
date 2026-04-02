using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // 1. THE COMMITMENT (Purchase Order)
    public class PurchaseOrder
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }
        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }

        [Required]
        public Guid VendorId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; } = DateTime.Today;
        public Guid? LinkedSalesOrderId { get; set; }

        [Required]
        public Guid CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ExchangeRate { get; set; } = 1;
        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Open;
        public string? ConvertedFromRequestNumber { get; set; }

        // --- NEW: TAX & DISCOUNT PROPERTIES ---
        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; } // Maps to Input VAT / Tax Receivable (Asset)

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountPercentage { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; } = 0;

        public Guid? DiscountGlAccountId { get; set; } // Maps to Discount Received (Income/Credit)

        public List<PurchaseOrderLine> Lines { get; set; } = new();

        public bool HasReceipt { get; set; } = false;
        public bool IsFullyReceived { get; set; } = false;
        public bool IsInvoicePosted { get; set; } = false;
        public bool IsFullyPaid { get; set; } = false;

        // --- UI COMPUTED HELPERS ---
        [NotMapped] public decimal GrandTotalForeign { get; set; }
    }

    public class PurchaseOrderLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PurchaseOrderId { get; set; }
        public Guid ItemId { get; set; }
        public decimal QuantityOrdered { get; set; }
        public decimal UnitCost { get; set; } // The Agreed Price
    }

    // 2. THE PHYSICAL RECEIPT (GRN)
    public class GoodsReceipt
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; } // <--- ADDED

        [Required]
        public Guid PurchaseOrderId { get; set; }
        public string GrnNumber { get; set; }
        public DateTime DateReceived { get; set; } = DateTime.Today;
        public Guid InventoryGlAccountId { get; set; }
        public List<GoodsReceiptLine> Lines { get; set; } = new();
    }

    public class GoodsReceiptLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GoodsReceiptId { get; set; }
        public Guid PurchaseOrderLineId { get; set; } // Link to specific PO Line
        public decimal QuantityReceived { get; set; }
        [NotMapped] public decimal MaxAllowed { get; set; }
    }
    public class ItemCostHistory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; }
        public DateTime DateChanged { get; set; } = DateTime.UtcNow;

        // The Snapshot
        [Column(TypeName = "decimal(18,2)")]
        public decimal OldQty { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal OldWacc { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal NewQtyIn { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal NewCostIn { get; set; } // The cost of the new batch

        [Column(TypeName = "decimal(18,4)")]
        public decimal ResultingWacc { get; set; }

        public string Reference { get; set; } = string.Empty; // e.g. "GRN-1001"
    }
    // 3. THE FINANCIAL LIABILITY (Vendor Bill)
    public class VendorBill
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid VendorId { get; set; }
        [Required]
        public Guid CompanyId { get; set; }
        public Guid? PurchaseOrderId { get; set; } 
        public Guid AccountsPayableGlId { get; set; }
        public bool IsDirectBill { get; set; } = false;
        public string ExternalInvoiceNumber { get; set; } = "";
        public DateTime BillDate { get; set; }
        public Guid CurrencyId { get; set; } 

        [Column(TypeName = "decimal(18,6)")]
        public decimal ExchangeRate { get; set; } = 1; 
        [Column(TypeName = "decimal(18,6)")]
        public decimal TotalAmountForeign { get; set; }
        public decimal TotalAmount { get; set; }

        // 3-WAY MATCH STATUS
        public BillMatchStatus MatchStatus { get; set; } = BillMatchStatus.Pending;
        public string MatchVarianceReason { get; set; } = "";
        public string? Description { get; set; }
        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; } 
        public List<VendorBillLine> Lines { get; set; } = new();
        public bool IsPosted { get; set; } = false;
        public DateTime? PostedDate { get; set; }
        public virtual List<VendorPayment> Payments { get; set; } = new();
        [NotMapped] public decimal AmountPaid => Payments?.Sum(p => p.Amount) ?? 0;
        [NotMapped] public decimal BalanceDue => TotalAmount - AmountPaid;
        [NotMapped] public bool IsFullyPaid => IsPosted && TotalAmount > 0 && BalanceDue <= 0;

    }
    public class VendorPayment
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid VendorBillId { get; set; }
        [ForeignKey(nameof(VendorBillId))]
        public VendorBill? VendorBill { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; } // Amount paid in base currency

        public Guid BankGlAccountId { get; set; }
        public string Reference { get; set; } = string.Empty;
    }

    public class VendorBillLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid VendorBillId { get; set; }
        public Guid? ItemId { get; set; }

        // FLEXIBILITY: User selects the Expense/Asset Account per line
        public Guid ExpenseGlAccountId { get; set; }
        public string? Description { get; set; }

        public decimal QuantityBilled { get; set; }
        public decimal UnitCostBilled { get; set; }
        public decimal LineTotal => QuantityBilled * UnitCostBilled;
    }
    public class WaccHistory
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; }
        public DateTime DateChanged { get; set; } = DateTime.UtcNow;

        // The Event
        public string Reference { get; set; } = ""; // e.g. "GRN-1001"

        // The Math
        [Column(TypeName = "decimal(18,2)")]
        public decimal OldQty { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal OldWacc { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal IncomingQty { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal IncomingCost { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal NewWacc { get; set; }
    }
    
    public enum BillMatchStatus
    {
        Pending,
        Matched,
        Variance,
        NoPoLinked
    }
    public enum PurchaseOrderStatus
    {
        Open,
        Request,
        PartiallyReceived,
        Closed,
        Cancelled
    }
}