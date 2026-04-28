using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        // Links user to a Company (Nullable for SuperAdmin)
        public Guid? CompanyDetailsId { get; set; }

        public string FullName => $"{FirstName} {LastName}";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}