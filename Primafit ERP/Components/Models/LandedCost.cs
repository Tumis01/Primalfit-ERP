using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class LandedCostType
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public string Name { get; set; } = string.Empty; 
        public Guid ClearingAccountId { get; set; }

        // Default Allocation Method
        public AllocationMethod DefaultMethod { get; set; } = AllocationMethod.ByValue;
    }

    public enum AllocationMethod
    {
        ByValue,    
        ByQuantity, 
        Manual      
    }

    public class GrnLandedCost
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GoodsReceiptId { get; set; }
        public Guid LandedCostTypeId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public AllocationMethod AllocationMethod { get; set; }
    }
}