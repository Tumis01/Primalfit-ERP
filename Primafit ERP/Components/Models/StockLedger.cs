using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum StockMovementType { Purchase, Sale, TransferIn, TransferOut, Adjustment }

    public class StockLedger
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;

        [Required] public Guid ItemId { get; set; }
        [Required] public Guid WarehouseId { get; set; }

        public StockMovementType Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QuantityChanged { get; set; } // + for In, - for Out

        [Column(TypeName = "decimal(18,4)")]
        public decimal CostAtTime { get; set; } // Snapshot of WACC at this moment

        public string Reference { get; set; } = string.Empty; // e.g., "INV-1001" or "PO-55"
    }
}