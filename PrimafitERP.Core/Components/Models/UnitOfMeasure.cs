using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    public class UnitOfMeasure
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public string ConversionFactor { get; set; } = string.Empty;
    }
}