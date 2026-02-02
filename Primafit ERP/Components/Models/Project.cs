using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
   //  ProjectModel Attributes
    public class Project
    {
        [Key]
        public Guid Id { get; set; } 

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } //  Unique reference e.g. PRJ-001

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } //  AccountName of job

        public decimal ProjectBudget { get; set; } //  Ceiling for specific activity

        public bool Status { get; set; } = true; //  Toggle for active/inactive
    }
}