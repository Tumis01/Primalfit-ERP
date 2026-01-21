using Microsoft.AspNetCore.Identity;

namespace Primafit_ERP.Components.Models
{
    public class ApplicationRole : IdentityRole
    {
        public ApplicationRole() : base() { }
        public ApplicationRole(string roleName, string? description = null) : base(roleName)
        {
            Description = description;
            CreatedDate = DateTime.UtcNow;
        }

        public string? Description { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}