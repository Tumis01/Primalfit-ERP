using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum CostingMethod
    {
        WACC, // Default
        StandardCosting,
        UserSpecified,
        FIFO,
        LIFO,
        MostRecentCost
    }
    public class Item
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [Required] public Guid CompanyId { get; set; }

        [Required] public string SKU { get; set; } = string.Empty;
        [Required] public string Name { get; set; } = string.Empty;
        public bool IsService { get; set; }
        public string UoM { get; set; } = "Each";

        public Guid? UomId { get; set; }
        [ForeignKey(nameof(UomId))]
        public virtual UnitOfMeasure? PrimaryUom { get; set; }
        public Guid? AlternateUomId { get; set; }
        [ForeignKey(nameof(AlternateUomId))]
        public virtual UnitOfMeasure? AlternateUom { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal AlternateUomConversionFactor { get; set; } = 1m;
        [Column(TypeName = "decimal(18,4)")]
        public decimal ReorderLevel { get; set; } = 10;
        public CostingMethod CostingType { get; set; } = CostingMethod.WACC;

        [Column(TypeName = "decimal(18,4)")]
        public decimal WeightedAverageCost { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal StandardCost { get; set; } 

        [Column(TypeName = "decimal(18,4)")]
        public decimal UserSpecifiedCost { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal MostRecentCost { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal SellingPrice { get; set; } 
        public Guid? CategoryId { get; set; }
        public virtual ItemCategory? Category { get; set; }

        // --- GL MAPPING (Where does the money go?) ---
        public Guid InventoryAssetAccountId { get; set; } // Dr Inventory (Asset)
        public Guid CostOfGoodsSoldAccountId { get; set; } // Dr COGS (Expense)
        public Guid SalesIncomeAccountId { get; set; }    // Cr Revenue (Income)
        public Guid AdjustmentExpenseAccountId { get; set; } // Dr Theft/Damage (Expense)
        public virtual List<ItemCostHistory> CostHistory { get; set; } = new();

        [NotMapped]
        public decimal CurrentDisplayCost => CostingType switch
        {
            CostingMethod.WACC => WeightedAverageCost,
            CostingMethod.StandardCosting => StandardCost,
            CostingMethod.UserSpecified => UserSpecifiedCost,
            CostingMethod.MostRecentCost => MostRecentCost,
            _ => WeightedAverageCost
        };
    }
    public class ItemCategory
    {
        [Key] public Guid Id { get; set; } 
        [Required] public Guid CompanyId { get; set; }

        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
        public bool IsService { get; set; }

        // --- GL TEMPLATE MAPPING ---
        public Guid SalesIncomeAccountId { get; set; } // Cr Revenue (Income)
        public Guid CostOfGoodsSoldAccountId { get; set; } // Dr COGS (Expense)

        // if IsService == false
        public Guid? InventoryAssetAccountId { get; set; } // Dr Inventory (Asset)
        public Guid? AdjustmentExpenseAccountId { get; set; } // Dr Theft/Damage (Expense)
    }
}
