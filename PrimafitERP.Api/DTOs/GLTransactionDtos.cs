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
}
