using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum ReconStatus { Open, Reconciled }

    public class BankReconciliation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }

        [Required]
        public Guid BankAccountId { get; set; } // Link to GL Chart of Accounts

        public DateOnly StatementDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal StatementEndingBalance { get; set; } // External Reality

        [Column(TypeName = "decimal(18,2)")]
        public decimal BookBalanceAtDate { get; set; } // Internal GL Balance Snapshot

        public ReconStatus Status { get; set; } = ReconStatus.Open;

        [Required]
        public string PreparedByUserId { get; set; } = string.Empty; // "Maker"
        public string? ApprovedByUserId { get; set; } // "Checker"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovedAt { get; set; }

        public virtual List<BankStatementLine> StatementLines { get; set; } = new();
    }

    public class BankStatementLine
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReconciliationId { get; set; }
        [ForeignKey(nameof(ReconciliationId))]
        public BankReconciliation? Reconciliation { get; set; }

        public DateOnly TransactionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Reference { get; set; } // Cheque No, Transfer Ref

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; } // Positive = Deposit, Negative = Withdrawal

        public bool IsCleared { get; set; } = false; // Matched flag

        // Link to GL (The Bridge)
        public Guid? MatchedGLTransactionId { get; set; }
    }

    // Data Transfer Object for the UI
    public class ReconViewModel
    {
        public BankReconciliation Header { get; set; } = new();

       
        public List<BankStatementLine> UnmatchedBankLines => Header.StatementLines.Where(x => !x.IsCleared).ToList();

        public List<GLTransaction> OutstandingGLTransactions { get; set; } = new();

        // Summary Calculations
        public decimal DepositsInTransit => OutstandingGLTransactions.Where(t => (t.Debit - t.Credit) > 0).Sum(t => t.Debit - t.Credit);
        public decimal OutstandingChecks => OutstandingGLTransactions.Where(t => (t.Debit - t.Credit) < 0).Sum(t => t.Credit - t.Debit); // Absolute value

        // The Formula: Bank + InTransit - Outstanding
        public decimal AdjustedBankBalance => Header.StatementEndingBalance + DepositsInTransit - OutstandingChecks;

        // Difference
        public decimal Difference => AdjustedBankBalance - Header.BookBalanceAtDate;
    }
}