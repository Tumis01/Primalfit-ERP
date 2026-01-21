using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Primafit_ERP.Components.Models
{
    // ENUMS
    public enum AccountType { Asset = 0, Liability = 1, Equity = 2, Revenue = 3, Expense = 4 }
    public enum NormalBalance { Debit = 0, Credit = 1 }

    public class ChartOfAccount
    {
        [Key]
        public Guid AccountId { get; set; }

        public Guid CompanyDetailsId { get; set; }

        [Required(ErrorMessage = "Account Code is required")]
        [StringLength(20)]
        public string AccountCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Account Name is required")]
        [StringLength(100)]
        public string AccountName { get; set; } = string.Empty;

        [Required]
        public AccountType Type { get; set; }

        // PARENT-CHILD RELATIONSHIP
        public Guid? ParentAccountId { get; set; }

        // Self-referencing navigation property
        [ForeignKey("ParentAccountId")]
        [JsonIgnore] // Prevent cycles in JSON
        public virtual ChartOfAccount? ParentAccount { get; set; }

        [JsonIgnore]
        public virtual ICollection<ChartOfAccount> SubAccounts { get; set; } = new List<ChartOfAccount>();

        public int Level { get; set; } = 1; // 1 = Class, 2 = Type, 3 = Main, 4 = Account

        public bool IsParent { get; set; } = false; // If true, cannot post transactions
        public bool IsActive { get; set; } = true;
        public NormalBalance NormalBalance { get; set; }
        public bool AllowManualEntry { get; set; } = true;
        public string? Description { get; set; }
    }
}