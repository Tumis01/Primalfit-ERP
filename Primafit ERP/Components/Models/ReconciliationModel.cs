using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum ReconType
    {
        Manual,
        Automatic
    }

    public enum ReconStatus
    {
        Draft,
        Finalized
    }

    public class BankReconciliation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid BankAccountId { get; set; } // FK to GLChartOfAccount

        public DateOnly StatementDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal StatementEndingBalance { get; set; }

        public ReconType Type { get; set; }
        public ReconStatus Status { get; set; } = ReconStatus.Draft;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovedAt { get; set; }
        public string PreparedByUserId { get; set; } = string.Empty;

        // Navigation for Auto-Match mode (CSV Lines)
        public List<BankStatementLine> StatementLines { get; set; } = new();
    }

    public class BankStatementLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ReconciliationId { get; set; }

        public DateOnly Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Reference { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; } // Positive = Deposit, Negative = Payment

        // Matching Logic
        public bool IsMatched { get; set; } // For Auto Mode
        public Guid? MatchedGLTransactionId { get; set; } // Link to GL
    }

    // This ViewModel is required by your Razor view
    public class ReconViewModel
    {
        public BankReconciliation Reconciliation { get; set; } = new();
        public List<GLTransaction> CandidateGLTransactions { get; set; } = new();
    }
    
}