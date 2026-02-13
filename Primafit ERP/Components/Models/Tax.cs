using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Tax
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    public string TaxName { get; set; }

    [Required]
    public string TaxCode { get; set; }

    [Column(TypeName = "decimal(9,4)")]
    public decimal Per { get; set; }
    public Guid? GLAccountId { get; set; }
}
