using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class Warehouse
    {
        public Guid ID { get; set; }
        public string Name { get; set; }
        public bool IsInTransist { get; set; }
        public string? Location { get; set; }
        [Required]
        public Guid CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public CompanyDetails? Company { get; set; }
    }
}
