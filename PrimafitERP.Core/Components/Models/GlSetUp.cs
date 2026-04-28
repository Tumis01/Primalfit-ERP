using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // Enum remains unchanged (Enums typically use int/byte)
    public enum GLAccountClass
    {
        Asset = 1,
        Liability = 2,
        Capital = 3,
        Revenue = 4,
        Expenses = 5
    }

    public class GLAccountType
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }

        [Required]
        public string Code { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public GLAccountClass Class { get; set; }

        [NotMapped]
        public string FullName => $"[{Class}] {Code} - {Name}";
    }


    public class GLMainAccount
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }

        [Required]
        public string Code { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public Guid AccountTypeId { get; set; }

        [ForeignKey(nameof(AccountTypeId))]
        public virtual GLAccountType? AccountType { get; set; }

        [NotMapped]
        public string FullName => $"{Code} - {Name}";
    }


    public class GLChartOfAccount
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }

        [Required]
        public string AccountCode { get; set; } = string.Empty;

        [Required]
        public string AccountName { get; set; } = string.Empty;

        [Required]
        public Guid MainAccountId { get; set; }

        [ForeignKey(nameof(MainAccountId))]
        public virtual GLMainAccount? MainAccount { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }

}