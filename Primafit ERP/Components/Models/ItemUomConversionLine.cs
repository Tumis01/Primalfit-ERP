using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models;

/// <summary>Item-specific UOM option. The factor expresses one primary UOM in the selected UOM.</summary>
public class ItemUomConversionLine
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required]
    public Guid ItemId { get; set; }
    [ForeignKey(nameof(ItemId))]
    public Item? Item { get; set; }
    [Required]
    public Guid UomId { get; set; }
    [ForeignKey(nameof(UomId))]
    public UnitOfMeasure? Uom { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    // Kept on the existing database column for compatibility. Its business
    // meaning is now: 1 item primary UOM = this many selected UOMs.
    public decimal ConversionFactorToBase { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
}
