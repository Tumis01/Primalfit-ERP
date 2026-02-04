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

        // --- GL MAPPING (Where does the money go?) ---
        public Guid InventoryAssetAccountId { get; set; } // Dr Inventory (Asset)
        public Guid CostOfGoodsSoldAccountId { get; set; } // Dr COGS (Expense)
        public Guid SalesIncomeAccountId { get; set; }    // Cr Revenue (Income)
        public Guid AdjustmentExpenseAccountId { get; set; } // Dr Theft/Damage (Expense)
    }
}