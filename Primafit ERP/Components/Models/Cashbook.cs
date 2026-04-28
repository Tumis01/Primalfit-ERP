using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    // BatchStatus Enum (Keeping as per your request)
    public class CashbookBatch
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid BankSegCoaId { get; set; } 

        [Required]
        [MaxLength(50)]
        public string BatchReference { get; set; } = "";

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public string CreatedByUserId { get; set; } = "";

        // Balances
        public decimal OpeningBalance { get; set; }


        // Sum of Debits (Money In)
        public decimal TotalDebits { get; set; }

        // Sum of Credits (Money Out)
        public decimal TotalCredits { get; set; }
        public bool IsForeignCurrency { get; set; } = false;
        public Guid? CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ExchangeRate { get; set; } = 1;

        [NotMapped]
        public decimal ClosingBalance => OpeningBalance + TotalDebits - TotalCredits;

        public BatchStatus Status { get; set; } = BatchStatus.Draft;
        public Guid? PostedGLBatchId { get; set; }

        public virtual List<CashbookEntry> Entries { get; set; } = new();
    }

    public class CashbookEntry
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CashbookBatchId { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.Today;

        [Required]
        public string Description { get; set; } = "";

        // REPLACED Enum with explicit Accounting Columns
        [Column(TypeName = "decimal(18,2)")]
        public decimal Debit { get; set; }  // Money In

        [Column(TypeName = "decimal(18,2)")]
        public decimal Credit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ForeignDebit { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal ForeignCredit { get; set; }
        public Guid OffsetSegCoaId { get; set; } // was OffsetAccountId


        [Required(ErrorMessage = "Reference is required")] 
        [MaxLength(50)]
        public string Reference { get; set; } = "";
        public Guid? ProjectId { get; set; }
    }
}