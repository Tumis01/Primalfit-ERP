using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    public class RegisterTenantDto
    {
        // --- COMPANY DETAILS ---
        [Required]
        [Display(Name = "Company Name")]
        public string CompanyName { get; set; } = string.Empty;

        [Required, EmailAddress]
        [Display(Name = "Company Email")]
        public string CompanyEmail { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Company Type")]
        public CompanyType Type { get; set; } = CompanyType.Corporation; // Default

        // --- ADMIN DETAILS ---
        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required, Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}