using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum ReturnStatus { Draft, Posted, Void }

    public class PurchaseReturn
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid VendorId { get; set; }
        // We generally don't add the Vendor object here to keep the graph simple, 

        [Required]
        public Guid VendorBillId { get; set; }
        [ForeignKey("VendorBillId")]
        public VendorBill? VendorBill { get; set; } // Link to source bill

        [Required]
        public Guid WarehouseId { get; set; }

        public string ReturnNumber { get; set; } = "";
        public DateTime ReturnDate { get; set; } = DateTime.Today;
        public string Reason { get; set; } = "";

        // Financials
        public Guid CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ExchangeRate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        public ReturnStatus Status { get; set; } = ReturnStatus.Draft;

        // Audit
        public Guid CreatedByUserId { get; set; }
        public Guid? PostedByUserId { get; set; }
        public DateTime? PostedAt { get; set; }

        public List<PurchaseReturnLine> Lines { get; set; } = new();
    }

    public class PurchaseReturnLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        // --- 1. Link to Parent Return (Fixes your specific error) ---
        public Guid PurchaseReturnId { get; set; }
        [ForeignKey("PurchaseReturnId")]
        public PurchaseReturn? PurchaseReturn { get; set; }

        // --- 2. Link to Vendor Bill Line (Required for validation logic) ---
        public Guid VendorBillLineId { get; set; }
        [ForeignKey("VendorBillLineId")]
        public VendorBillLine? VendorBillLine { get; set; }

        public Guid ItemId { get; set; }
        public string ItemName { get; set; } = "";

        [Column(TypeName = "decimal(18,6)")]
        public decimal QtyReturning { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal UnitCost { get; set; }

        public decimal LineTotal => QtyReturning * UnitCost;
    }
}