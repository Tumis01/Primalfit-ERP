using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    // Used for creating a user internally
    public class UserDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
        public string RoleDisplayName { get; set; } = string.Empty; // e.g. "Manager"

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        // This is auto-filled by the system context, not the user input
        public Guid CompanyDetailsId { get; set; }
    }

    // Used for the Grid View
    public class UserDisplayDto
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; } 
    }
}