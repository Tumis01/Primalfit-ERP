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

        [Column(TypeName = "decimal(18,4)")]
        public decimal BudgetedRevenue { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal BudgetedCost { get; set; }

        public ProjectStatus Status { get; set; } = ProjectStatus.Proposed;
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime? EndDate { get; set; }
    }
}