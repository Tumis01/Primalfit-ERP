using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class Customer
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public string Name { get; set; } = "";
        [Required(ErrorMessage = "Email is required")]
        public string Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }

        // --- ACCOUNTING SETUP ---

        [Required(ErrorMessage = "Default Currency is required")]
        public Guid CurrencyId { get; set; }

        [ForeignKey("CurrencyId")]
        public Currency? DefaultCurrency { get; set; }

        // Link to GL for Accounts Receivable (Control Account)
        public Guid? ReceivablesAccountId { get; set; }
        public Guid? CustomerGroupId { get; set; }

        [ForeignKey("CustomerGroupId")]
        public CustomerGroup? Group { get; set; }
    }
}