using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // 1. Account Type (Fixed ID to match Excel)
    public class AccountType1
    {
        // "None" stops the DB from auto-generating IDs, preventing the "Identity Insert" error
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        // Fields from your Excel logic
        public bool IsBalanceSheet { get; set; } // True = BS, False = P&L
        public bool IsDebit { get; set; }        // True = Debit Normal, False = Credit Normal
    }

    // 2. Segment Structure (Definitions)
    public class SegmentDefinition
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int SegmentNumber { get; set; } // 1 to 6

        [Required]
        public string SegmentName { get; set; } = string.Empty;

        public int Length { get; set; } = 3;
        public bool IsActive { get; set; } = true;
    }

    // 3. Segment Values (The actual data, e.g., "100 - Sales Dept")
    public class SegmentValue
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }

        public int SegmentNumber { get; set; } // Links to Definition (2-6)

        [Required, MaxLength(20)]
        public string Value { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;
    }

    // 4. Main Account (The Header / Segment 1)
    public class MainAccount
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }

        [Required, MaxLength(20)]
        public string AccountCode { get; set; } = string.Empty;

        [Required]
        public string AccountName { get; set; } = string.Empty;

        public int AccountType1Id { get; set; }
        [ForeignKey("AccountType1Id")]
        public virtual AccountType1? AccountType { get; set; }
    }

    // 5. The Final Combined GL Account
    public class SegmentedAccount
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }

        // Segment 1 is the Main Account
        public Guid MainAccountId { get; set; }
        [ForeignKey("MainAccountId")]
        public virtual MainAccount? MainAccount { get; set; }

        // Optional Sub-Segments
        public Guid? Segment2ValueId { get; set; }
        [ForeignKey("Segment2ValueId")] public virtual SegmentValue? Segment2 { get; set; }

        public Guid? Segment3ValueId { get; set; }
        [ForeignKey("Segment3ValueId")] public virtual SegmentValue? Segment3 { get; set; }

        public Guid? Segment4ValueId { get; set; }
        [ForeignKey("Segment4ValueId")] public virtual SegmentValue? Segment4 { get; set; }

        public Guid? Segment5ValueId { get; set; }
        [ForeignKey("Segment5ValueId")] public virtual SegmentValue? Segment5 { get; set; }

        public Guid? Segment6ValueId { get; set; }
        [ForeignKey("Segment6ValueId")] public virtual SegmentValue? Segment6 { get; set; }

        // The Generated String (e.g. 4000-100-01)
        [Required]
        public string AccountCodeString { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}