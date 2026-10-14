using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum ProjectStatus { Proposed, Active, Completed, OnHold, Cancelled }

    public class Project
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ProjectCode { get; set; } = string.Empty; // e.g., "PRJ-2026-001"

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        // The Projects table predates the accounting project dimension. Existing
        // rows remain available for compatibility but the new module only manages
        // rows explicitly created for project reporting.
        public bool IsAccountingProject { get; set; }

        public bool IsActive { get; set; } = true;

        [Column(TypeName = "decimal(18,4)")]
        public decimal BudgetedRevenue { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal BudgetedCost { get; set; }

        public ProjectStatus Status { get; set; } = ProjectStatus.Proposed;
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime? EndDate { get; set; }
    }
}
