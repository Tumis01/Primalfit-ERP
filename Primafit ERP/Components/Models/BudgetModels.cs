using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class BudgetHeader
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid AccountingPeriodId { get; set; }

        [Required]
        public string BudgetName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public List<BudgetLine> Lines { get; set; } = new();
        public List<BudgetTransferLine> TransferLines { get; set; } = new();
    }

    public class BudgetLine
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BudgetHeaderId { get; set; }
        public Guid GlAccountId { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal LimitAmount { get; set; }
        public List<BudgetPeriodAllocation> PeriodAllocations { get; set; } = new();
    }

    public class BudgetPeriodAllocation
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BudgetLineId { get; set; }
        public Guid AccountingPeriodId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Amount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ForecastAmount { get; set; }

        [NotMapped] public string PeriodName { get; set; } = "";
    }


    public class BudgetTransferLine
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BudgetHeaderId { get; set; }

        public Guid FromGlAccountId { get; set; }
        public Guid ToGlAccountId { get; set; }  

        [Column(TypeName = "decimal(18,2)")]
        public decimal LimitAmount { get; set; } 

        public List<BudgetTransferPeriodAllocation> PeriodAllocations { get; set; } = new();
    }

    public class BudgetTransferPeriodAllocation
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BudgetTransferLineId { get; set; }
        public Guid AccountingPeriodId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Amount { get; set; }
        [NotMapped] public string PeriodName { get; set; } = "";
    }
}