using System.ComponentModel.DataAnnotations;

namespace PrimafitERP.Api.DTOs
{
    
    public class CreatePurchaseOrderDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid VendorId { get; set; }
        [Required] public Guid CurrencyId { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public decimal ExchangeRate { get; set; } = 1;

        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }
        public decimal DiscountPercentage { get; set; } = 0;
        public decimal DiscountAmount { get; set; } = 0;
        public Guid? DiscountGlAccountId { get; set; }

        [Required] public List<CreatePurchaseOrderLineDto> Lines { get; set; } = new();
    }

    public class CreatePurchaseOrderLineDto
    {
        [Required] public Guid ItemId { get; set; }
        [Required] public decimal Quantity { get; set; }
        [Required] public decimal UnitCost { get; set; }
    }


    // --- 1. GOODS RECEIPT (GRN) DTOs ---
    public class CreateGoodsReceiptDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid WarehouseId { get; set; }
        [Required] public Guid CreditLiabilityGlAccountId { get; set; } // The GR/IR Clearing Account
        public string GrnNumber { get; set; } = string.Empty;
        public DateTime DateReceived { get; set; } = DateTime.UtcNow;
        [Required] public List<GoodsReceiptLineDto> Lines { get; set; } = new();
    }

    public class GoodsReceiptLineDto
    {
        [Required] public Guid PurchaseOrderLineId { get; set; }
        [Required] public decimal QuantityReceived { get; set; }
    }

    // --- 2. ACCOUNTS PAYABLE DTOs ---
    public class CreateManualVendorBillDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid VendorId { get; set; }
        [Required] public Guid AccountsPayableGlId { get; set; } // AP Liability Account
        [Required] public Guid CurrencyId { get; set; }
        public Guid? PurchaseOrderId { get; set; } // Optional: If linking to PO manually
        public decimal ExchangeRate { get; set; } = 1;
        public string ExternalInvoiceNumber { get; set; } = string.Empty;
        public DateTime BillDate { get; set; } = DateTime.UtcNow;
        [Required] public List<VendorBillLineDto> Lines { get; set; } = new();
    }

    public class VendorBillLineDto
    {
        [Required] public Guid ItemId { get; set; }
        [Required] public Guid ExpenseGlAccountId { get; set; } // COGS or Inventory Asset Account
        [Required] public decimal QuantityBilled { get; set; }
        [Required] public decimal UnitCostBilled { get; set; }
    }

    public class PayVendorBillDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid BankGlAccountId { get; set; } // Asset account being reduced
        [Required] public decimal Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string Reference { get; set; } = string.Empty;
    }
}
