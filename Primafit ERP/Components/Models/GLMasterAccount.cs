using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    public class GLMasterAccount
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string AccountCode { get; set; } // e.g., "1000"

        [Required]
        [MaxLength(100)]
        public string AccountName { get; set; } // e.g., "Bank Accounts"

        [Required]
        public AccountType AccountType { get; set; } // Asset, Liability, etc.

        public bool IsActive { get; set; } = true;

        // Navigation Property: One Master has many Subs
        public virtual ICollection<GLSubAccount> SubAccounts { get; set; } = new List<GLSubAccount>();
    }
}