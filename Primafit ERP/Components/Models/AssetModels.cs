using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum AssetStatus { Active, FullyDepreciated, Sold, Scrapped }

    public enum DepreciationMethod
    {
        StraightLine,
        ImmediateWriteOff,
        NoDepreciation,
        DecliningBalance,
        UnitsOfUsage,
        SumOfYearsDigits
    }

    public class AssetCategory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;

        public DepreciationMethod DefaultDepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

        // Default GL Mappings
        public Guid? FixedAssetAccountId { get; set; }
        public Guid? AccumulatedDepreciationAccountId { get; set; }
        public Guid? DepreciationExpenseAccountId { get; set; }

        // Default Method Parameters
        [Column(TypeName = "decimal(18,4)")]
        public decimal? DecliningRate { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? DecliningFactor { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? EstimatedTotalUnits { get; set; }

        public string UnitOfMeasure { get; set; } = string.Empty; // e.g., "km", "hours"
    }

    public class FixedAsset
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required]
        public Guid CompanyId { get; set; }

        public Guid? AssetCategoryId { get; set; }

        [Required]
        public string AssetName { get; set; } = string.Empty;
        public string AssetTag { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        // --- FINANCIALS ---
        [Column(TypeName = "decimal(18,2)")]
        public decimal PurchaseCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SalvageValue { get; set; }

        public int UsefulLifeMonths { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CurrentBookValue { get; set; }

        public DateTime PurchaseDate { get; set; }
        public DateTime DepreciationStartDate { get; set; }
        public DateTime? LastDepreciationDate { get; set; }

        public AssetStatus Status { get; set; } = AssetStatus.Active;

        // --- DEPRECIATION SETTINGS ---
        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

        [Column(TypeName = "decimal(18,4)")]
        public decimal? DecliningRate { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? DecliningFactor { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? EstimatedTotalUnits { get; set; }

        // --- GL MAPPING ---
        public Guid FixedAssetAccountId { get; set; }
        public Guid AccumulatedDepreciationAccountId { get; set; }
        public Guid DepreciationExpenseAccountId { get; set; }
    }

    public class AssetUsageLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid FixedAssetId { get; set; }
        public DateTime PeriodDate { get; set; } // End of month date this applies to

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitsUsed { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AssetDepreciationHistory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid FixedAssetId { get; set; }
        public DateTime Date { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public DepreciationMethod MethodUsed { get; set; }
        public Guid GlBatchId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? UnitsUsed { get; set; } // For Units of Usage audit
        public string DetailsJson { get; set; } = string.Empty; // Audit trail of parameters used
    }
}