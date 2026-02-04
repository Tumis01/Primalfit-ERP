using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // 1. CLEAN WAREHOUSE (No Transit Property)
    public class Warehouse
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [Required] public Guid CompanyId { get; set; }

        [Required] public string Name { get; set; } = string.Empty;
        public string? Location { get; set; }
    }

    // 2. STOCK TRANSFER (Tracks the "Floating" Items)
    public enum TransferStatus { InTransit, Received, Cancelled }

    public class StockTransfer
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public string Reference { get; set; } = string.Empty; // e.g. TRF-1001

        // Source & Dest
        public Guid FromWarehouseId { get; set; }
        public Guid ToWarehouseId { get; set; }
        [ForeignKey(nameof(FromWarehouseId))] public Warehouse? FromWarehouse { get; set; }
        [ForeignKey(nameof(ToWarehouseId))] public Warehouse? ToWarehouse { get; set; }

        // Item Details
        public Guid ItemId { get; set; }
        [ForeignKey(nameof(ItemId))] public Item? Item { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? QuantityReceived {get; set; }

        // Transit Tracking
        public TransferStatus Status { get; set; } = TransferStatus.InTransit;

        public DateTime DateShipped { get; set; } = DateTime.UtcNow;
        public DateTime? DateReceived { get; set; }

        // Financial Tracking (To reverse the GL on receipt)
        public Guid TransitGLAccountId { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal ValueAtShipment { get; set; } // Snapshot of cost
    }
}