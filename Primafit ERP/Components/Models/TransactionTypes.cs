using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // =========================================================
    // 1. SYSTEM TRANSACTION TYPES (Includes New AR Adjustment)
    // =========================================================
    public enum SystemTransactionType
    {
        ArAdjustment = 1,          
        ApAdjustment = 2,          
        DirectPurchaseInvoice = 3,
        DirectSalesInvoice = 4,
        DiscountAllowed = 5,
        DiscountReceived = 6,
        SalesInvoice = 7,
        PurchaseInvoice = 8,
        CreditNote = 9,
        ReturnToVendor = 10,
        InventoryAdjustment = 13,
        CustomGlAdjustment = 14,
        GoodsReceipt = 15,
        DebitNote = 16,
        CustomerPayment = 17,
        VendorPayment = 18,
        ReceiptRefund = 19,
        ShipmentDispatch = 20
    }

    // =========================================================
    // 2. TRANSACTIONS GL MAPPING
    // =========================================================
    public class TransactionGlMapping
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public SystemTransactionType TransactionType { get; set; }

        // Optional custom template link (Null for core system txns, set for user-defined templates)
        public Guid? CustomTransactionTypeId { get; set; }
        [ForeignKey("CustomTransactionTypeId")]
        public CustomTransactionType? CustomTransactionType { get; set; }

        public Guid? OverrideDebitGlAccountId { get; set; }
        [ForeignKey("OverrideDebitGlAccountId")]
        public SegChartOfAccount? OverrideDebitAccount { get; set; }

        public Guid? OverrideCreditGlAccountId { get; set; }
        [ForeignKey("OverrideCreditGlAccountId")]
        public SegChartOfAccount? OverrideCreditAccount { get; set; }

        public bool IsActive { get; set; } = true;
        public string UpdatedByUserId { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // =========================================================
    // 3. NEW: CUSTOM TRANSACTION USER TYPES (Requirement #5)
    // =========================================================
    public class CustomTransactionType
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty; // e.g., "Director Loan", "Asset Write-Off"

        [StringLength(150)]
        public string Description { get; set; } = string.Empty;

        public string CreatedByUserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // =========================================================
    // 4. MIGRATION / ADJUSTMENT LINES DTO
    // =========================================================
    public class OpeningBalanceLineDto
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid EntityId { get; set; } // CustomerId or VendorId
        public string EntityName { get; set; } = string.Empty;
        public decimal BalanceAmount { get; set; }
    }
    public class InventoryAdjustmentLineDto
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public Guid WarehouseId { get; set; }
        public bool IsDebitInventory { get; set; } = true; // true = Debit Item Account, false = Credit Item Account
        public decimal QuantityChange { get; set; } // Positive for increase, Negative for decrease
        public decimal TotalValueChange { get; set; }
    }
    public class TransactionTypeOptionDto
    {
        public string ValueKey { get; set; } = string.Empty; // e.g., "SYS_1" or "CUST_guid"
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsCustom { get; set; }
        public SystemTransactionType SystemType { get; set; }
        public Guid? CustomTransactionTypeId { get; set; }
        public Guid? DefaultDebitAccountId { get; set; }
        public Guid? DefaultCreditAccountId { get; set; }
    }
}