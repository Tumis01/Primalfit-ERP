using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    public class ApplicationRole : IdentityRole
    {
        public ApplicationRole() : base() { }

        public ApplicationRole(string roleName, Guid companyId, string? description = null) 
        {
            CompanyId = companyId;
            DisplayName = roleName;
            // We ensure uniqueness in the DB by combining CompanyId + Name
            Name = $"{companyId}_{roleName}";
            Description = description;
            CreatedDate = DateTime.UtcNow;
        }

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public string DisplayName { get; set; } 

        public string? Description { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}