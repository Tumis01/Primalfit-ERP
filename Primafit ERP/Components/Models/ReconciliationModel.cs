using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum ReconStatus { Draft, Approved }
    public enum ReconType { Automatic, Manual } // Added Enum

    public class BankReconciliation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid AccountingPeriodId { get; set; }

        [Required]
        public Guid BankAccountId { get; set; }

        public DateOnly StatementDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal StatementEndingBalance { get; set; } 

        public ReconStatus Status { get; set; } = ReconStatus.Draft;
        public ReconType Type { get; set; } = ReconType.Automatic;

        [Required]
        public string PreparedByUserId { get; set; } = string.Empty;

        public string? ApprovedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovedAt { get; set; }

        public virtual List<BankStatementLine> StatementLines { get; set; } = new();
    }

    
    public class BankStatementLine
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid ReconciliationId { get; set; }
        [ForeignKey(nameof(ReconciliationId))] public BankReconciliation? Reconciliation { get; set; }
        public DateOnly Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Reference { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
        public bool IsMatched { get; set; } = false;
        public Guid? MatchedGLTransactionId { get; set; }
    }

    public class ReconViewModel
    {
        public BankReconciliation Reconciliation { get; set; } = new();

        
        public List<GLTransaction> CandidateGLTransactions { get; set; } = new();
    }
}