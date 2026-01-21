using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    // Used in the "Add User" form
    public class UserDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required] // This binds to the dynamic dropdown
        public string RoleName { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        public Guid? CompanyDetailsId { get; set; }
    }

    // Used in the Login Page
    public class LoginDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    // Used to display users in the list
    public class UserDisplayDto
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public string CompanyName { get; set; }
        public bool IsActive { get; set; }
    }
}