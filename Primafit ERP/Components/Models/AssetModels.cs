using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum AssetStatus { Active, FullyDepreciated, Sold, Scrapped }

    public class FixedAsset
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public string AssetName { get; set; } = string.Empty;
        public string AssetTag { get; set; } = string.Empty; // e.g., "FA-001"
        public string SerialNumber { get; set; } = string.Empty;

        // --- FINANCIALS ---
        [Column(TypeName = "decimal(18,2)")]
        public decimal PurchaseCost { get; set; } // Original Cost

        [Column(TypeName = "decimal(18,2)")]
        public decimal SalvageValue { get; set; } // Value at end of life (Scrap value)

        public int UsefulLifeMonths { get; set; } // e.g., 60 months (5 years)

        [Column(TypeName = "decimal(18,2)")]
        public decimal CurrentBookValue { get; set; } // Cost - Accumulated Depr

        public DateTime PurchaseDate { get; set; }
        public DateTime DepreciationStartDate { get; set; }
        public DateTime? LastDepreciationDate { get; set; }

        public AssetStatus Status { get; set; } = AssetStatus.Active;

        // --- GL MAPPING (Where does the money go?) ---
        public Guid FixedAssetAccountId { get; set; }           // Asset (Dr) - e.g. "Machinery"
        public Guid AccumulatedDepreciationAccountId { get; set; } // Contra-Asset (Cr)
        public Guid DepreciationExpenseAccountId { get; set; }  // Expense (Dr)
    }

    // AUDIT TRAIL: Tracks every monthly run
    public class AssetDepreciationHistory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid FixedAssetId { get; set; }
        public DateTime Date { get; set; } // The month being depreciated

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public Guid GlBatchId { get; set; } // Link to the GL Journal
    }
}