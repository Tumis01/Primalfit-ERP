using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum BatchStatus { Draft, Ready, Posted, Rejected }
    public enum BatchType { Standard, Migration, ReconciliationAdjustment }
    public enum JournalStatus { Draft, Posted }

    public class GLBatch
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }

        [Required]
        public Guid AccountingPeriodId { get; set; }

        [ForeignKey(nameof(AccountingPeriodId))]
        public AccountingPeriod? AccountingPeriod { get; set; }

        [Required]
        public string BatchName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public BatchStatus Status { get; set; } = BatchStatus.Draft;

        public BatchType Type { get; set; } = BatchType.Standard;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string CreatedByUserId { get; set; } = string.Empty;

        public string? ReleasedByUserId { get; set; }
        public DateTime? ReleasedAt { get; set; }

        public string? PostedByUserId { get; set; }
        public DateTime? PostedAt { get; set; }

        public string? RejectedByUserId { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }

        public virtual List<GLJournalHeader> Journals { get; set; } = new();
    }

    public class GLJournalHeader
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid AccountingPeriodId { get; set; }

        [Required]
        public string JournalNumber { get; set; } = string.Empty;

        public string? Narration { get; set; }

        public DateOnly TransactionDate { get; set; }

        public JournalStatus Status { get; set; } = JournalStatus.Draft;

        [Required]
        public Guid BatchId { get; set; }

        [ForeignKey(nameof(BatchId))]
        public GLBatch? Batch { get; set; }

        public virtual List<GLJournalLine> Lines { get; set; } = new();
    }

    public class GLJournalLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid HeaderId { get; set; }

        [ForeignKey(nameof(HeaderId))]
        public GLJournalHeader? Header { get; set; }

        [Required]
        public Guid AccountId { get; set; } // COA Id

        [Column(TypeName = "decimal(18,2)")]
        public decimal Debit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Credit { get; set; }

        public string? Reference { get; set; }
       
    }
    public class GLTransaction
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid AccountingPeriodId { get; set; }

        public DateOnly PostingDate { get; set; }

        [Required]
        public Guid BatchId { get; set; }

        [Required]
        public Guid JournalId { get; set; }

        [Required]
        public Guid AccountId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Debit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Credit { get; set; }

        public string? Narration { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Reconciliation lock
        public bool IsReconciled { get; set; } = false;
        public Guid? BankReconciliationId { get; set; }
        public DateTime? ReconciledAt { get; set; }
        public string? ReconciledByUserId { get; set; }
        public Guid? ProjectId { get; set; } // The User selection
        [ForeignKey(nameof(ProjectId))]
        public Project? Project { get; set; }
    }

    public class TrialBalanceRow
    {
        public Guid AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";

        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal NetBalance => TotalDebit - TotalCredit;
    }
    public class LedgerReportRow
    {
        public Guid AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public DateOnly PostingDate { get; set; }
        public string JournalNumber { get; set; } = "";
        public string? Narration { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
