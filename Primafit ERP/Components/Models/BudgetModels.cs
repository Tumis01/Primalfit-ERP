using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class BudgetHeader
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid AccountingPeriodId { get; set; } // e.g. "FY 2026"

        [Required]
        public string BudgetName { get; set; } = string.Empty; // e.g. "Master Budget 2026"
        public bool IsActive { get; set; } = true;

        public List<BudgetLine> Lines { get; set; } = new();
    }

    public class BudgetLine
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BudgetHeaderId { get; set; }

        public Guid GlAccountId { get; set; } // The Expense Account

        [Column(TypeName = "decimal(18,2)")]
        public decimal LimitAmount { get; set; } // Total Budget

        // We don't store "Used" here. We calculate it dynamically to ensure accuracy.
    }
}