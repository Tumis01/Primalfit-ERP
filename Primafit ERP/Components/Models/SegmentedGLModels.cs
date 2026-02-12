using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // Fixed lookup data (seeded) - should not be edited/deleted.
    public class SegAccountType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)] // IMPORTANT: we seed fixed IDs
        public int Id { get; set; }

        [Required, MaxLength(150)]
        public string Description { get; set; } = string.Empty;

        public bool IsBalanceSheet { get; set; }   // true = Balance Sheet, false = Income Statement
        public bool IsDebit { get; set; }          // true = Normal balance is Debit, false = Credit
    }

    public class Segment0
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }

    }
    public class Segment1
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }

    }
    public class Segment2
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }

    }
    public class Segment3
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }

    }
    public class Segment4
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }

    }
    public class Segment5
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }

    }

    public class SegChartOfAccount
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }

        // Selected segment row IDs (Segment0 required; others optional depending on config)
        [Required]
        public Guid Segment0Id { get; set; }

        public Guid? Segment1Id { get; set; }
        public Guid? Segment2Id { get; set; }
        public Guid? Segment3Id { get; set; }
        public Guid? Segment4Id { get; set; }
        public Guid? Segment5Id { get; set; }

        [Required, MaxLength(100)]
        public string AccountCode { get; set; } = string.Empty; // computed read-only in UI

        [Required, MaxLength(250)]
        public string Description { get; set; } = string.Empty; // computed initial but editable

        // FK to your seeded fixed table SegAccountTypes (int)
        [Required]
        public int SegAccountTypeId { get; set; }

        public bool AllowJournal { get; set; } = true;
    }
    public class SegCoaConfig
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }

        // Segment0 always active and required for COA
        [Required, MaxLength(50)]
        public string Segment0Name { get; set; } = "Segment 0";

        [MaxLength(50)] public string Segment1Name { get; set; } = "Segment 1";
        [MaxLength(50)] public string Segment2Name { get; set; } = "Segment 2";
        [MaxLength(50)] public string Segment3Name { get; set; } = "Segment 3";
        [MaxLength(50)] public string Segment4Name { get; set; } = "Segment 4";
        [MaxLength(50)] public string Segment5Name { get; set; } = "Segment 5";

        public bool Segment1Active { get; set; }
        public bool Segment2Active { get; set; }
        public bool Segment3Active { get; set; }
        public bool Segment4Active { get; set; }
        public bool Segment5Active { get; set; }
    }

}
