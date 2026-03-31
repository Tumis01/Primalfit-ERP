using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class Item
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [Required] public Guid CompanyId { get; set; }

        [Required] public string SKU { get; set; } = string.Empty;
        [Required] public string Name { get; set; } = string.Empty;
        public bool IsService { get; set; }
        public string UoM { get; set; } = "Each"; 
        [Column(TypeName = "decimal(18,2)")]
        public decimal ReorderLevel { get; set; } = 10; 

        [Column(TypeName = "decimal(18,4)")]
        public decimal WeightedAverageCost { get; set; } = 0; 

        [Column(TypeName = "decimal(18,2)")]
        public decimal SellingPrice { get; set; } = 0;
        public Guid? CategoryId { get; set; }
        public virtual ItemCategory? Category { get; set; }

        // --- GL MAPPING (Where does the money go?) ---
        public Guid InventoryAssetAccountId { get; set; } // Dr Inventory (Asset)
        public Guid CostOfGoodsSoldAccountId { get; set; } // Dr COGS (Expense)
        public Guid SalesIncomeAccountId { get; set; }    // Cr Revenue (Income)
        public Guid AdjustmentExpenseAccountId { get; set; } // Dr Theft/Damage (Expense)
        public virtual List<ItemCostHistory> CostHistory { get; set; } = new();
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

        // These will only be used if IsService == false
        public Guid? InventoryAssetAccountId { get; set; } // Dr Inventory (Asset)
        public Guid? AdjustmentExpenseAccountId { get; set; } // Dr Theft/Damage (Expense)
    }
}