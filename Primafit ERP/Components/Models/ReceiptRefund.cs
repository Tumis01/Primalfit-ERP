using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum ReceiptRefundStatus
    {
        Draft = 0,
        Posted = 1,
        Void = 2
    }

    public enum ReceiptRefundType
    {
        QuantityOnly = 0, // Product items return to inventory; cash stays on credit account
        PaymentOnly = 1,  // Direct cash layout to customer; items stay with buyer
        Both = 2          // Product items return to inventory AND cash is paid out
    }
    public class ReceiptRefund
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid SalesOrderId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid CurrencyId { get; set; }

        public string RefundNumber { get; set; } = string.Empty;
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public string Reason { get; set; } = string.Empty;

        public ReceiptRefundStatus Status { get; set; } = ReceiptRefundStatus.Draft;
        public ReceiptRefundType RefundType { get; set; } = ReceiptRefundType.Both;

        // Target bank asset ledger account required for Cash payouts (PaymentOnly or Both)
        public Guid? BankAccountId { get; set; }
        public Guid? DestinationWarehouseId { get; set; }

        public decimal ExchangeRate { get; set; } = 1.0m;
        public decimal TotalAmount { get; set; } // Representing the net cash value refunded out (if any)

        public Guid CreatedByUserId { get; set; }
        public Guid? PostedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PostedAt { get; set; }
        public Guid? GlBatchId { get; set; }
        public virtual SalesOrder? SalesOrder { get; set; }
        public virtual Customer? Customer { get; set; }
        public virtual Currency? Currency { get; set; }
        public virtual Warehouse? DestinationWarehouse { get; set; }
        public virtual ICollection<ReceiptRefundLine> Lines { get; set; } = new List<ReceiptRefundLine>();
    }
    public class ReceiptRefundLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid HeaderId { get; set; }
        public Guid SalesOrderLineId { get; set; }
        public Guid ItemId { get; set; }

        public decimal Quantity { get; set; } // Quantity returned or referenced
        public decimal UnitPrice { get; set; }
        public decimal LineTotal => Quantity * UnitPrice; // Gross value before discount adjustments

        // Unpersisted storage variables used to hold baseline boundaries in front-end grids
        [NotMapped] public decimal MaxAdjustableQty { get; set; }
        [NotMapped] public decimal MaxAdjustableAmount { get; set; }

        // Navigation Properties
        [ForeignKey("HeaderId")]
        public virtual ReceiptRefund? Header { get; set; }
        public virtual Item? Item { get; set; }
        public virtual SalesOrderLine? SalesOrderLine { get; set; }
        [NotMapped]
        public decimal OriginalShippedQty { get; set; }
        [NotMapped]
        public decimal PreviouslyReturnedQty { get; set; }
    }
}
