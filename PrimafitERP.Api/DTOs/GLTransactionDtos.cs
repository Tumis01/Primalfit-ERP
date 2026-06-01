using System.ComponentModel.DataAnnotations;

namespace PrimafitERP.Api.DTOs;

// ==========================================
// JOURNAL ENTRY DTOS
// ==========================================

public class CreateJournalDto
{
    [Required(ErrorMessage = "Journal description is required.")]
    public string Description { get; set; } = string.Empty;

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [Required]
    [MinLength(1, ErrorMessage = "A journal entry must contain at least one line item.")]
    public List<CreateJournalLineDto> Lines { get; set; } = new();
}

public class CreateJournalLineDto
{
    [Required(ErrorMessage = "Account selection is required.")]
    public Guid AccountId { get; set; }

    public string Description { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "Debit amount cannot be negative.")]
    public decimal Debit { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Credit amount cannot be negative.")]
    public decimal Credit { get; set; }
}

// ==========================================
// CASHBOOK BATCH & ENTRY DTOS
// ==========================================

public class CreateCashbookBatchDto
{
    [Required(ErrorMessage = "Bank Account selection is required.")]
    public Guid BankAccountId { get; set; }

    public bool IsForeignCurrency { get; set; }

    public Guid? CurrencyId { get; set; }

    [Range(0.000001, double.MaxValue, ErrorMessage = "Exchange rate must be greater than zero.")]
    public decimal ExchangeRate { get; set; } = 1.0m;

    public bool ClearAfterPost { get; set; }
}

public class CreateCashbookEntryDto
{
    [Required(ErrorMessage = "Transaction date is required.")]
    public DateTime TransactionDate { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Offset balancing GL Account is required.")]
    public Guid OffsetSegCoaId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Debit value cannot be negative.")]
    public decimal Debit { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Credit value cannot be negative.")]
    public decimal Credit { get; set; }
}

public class UpdateCashbookEntryDto : CreateCashbookEntryDto
{
    [Required(ErrorMessage = "Target Entry Identification is required for updates.")]
    public Guid Id { get; set; }
}