using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models;

public class WithholdingTax
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CompanyId { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    [Range(typeof(decimal), "0", "100")]
    public decimal PercentageValue { get; set; }

    [Required]
    public Guid WithholdingGlAccountId { get; set; }

    public bool IsSystemDefault { get; set; }
    [StringLength(40)]
    public string? DefaultKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
