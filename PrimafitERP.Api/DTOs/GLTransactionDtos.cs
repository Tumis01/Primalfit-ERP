using System.ComponentModel.DataAnnotations;

namespace PrimafitERP.Api.DTOs
{
    public class CreateJournalDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public string Description { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        [Required] public List<CreateJournalLineDto> Lines { get; set; } = new();
    }

    public class CreateJournalLineDto
    {
        [Required] public Guid AccountId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class CreateCashbookBatchDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid BankAccountId { get; set; } // The primary bank account for this batch
    }

    public class CreateCashbookEntryDto
    {
        [Required] public DateTime TransactionDate { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        [Required] public Guid OffsetSegCoaId { get; set; } // The account balancing the bank
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class UpdateCashbookEntryDto : CreateCashbookEntryDto
    {
        [Required] public Guid Id { get; set; }
    }
}
