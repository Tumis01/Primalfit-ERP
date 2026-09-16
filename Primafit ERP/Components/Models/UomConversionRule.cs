using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models;

/// <summary>One unit of a source UOM expressed in a target UOM.</summary>
public class UomConversionRule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required]
    public Guid CompanyId { get; set; }
    [Required]
    public Guid FromUomId { get; set; }
    [ForeignKey(nameof(FromUomId))]
    public UnitOfMeasure? FromUom { get; set; }
    [Required]
    public Guid ToUomId { get; set; }
    [ForeignKey(nameof(ToUomId))]
    public UnitOfMeasure? ToUom { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal ConversionFactor { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
}
