using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class CustomerGroup
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = "";

        public Guid? ReceivablesAccountId { get; set; }

    }
    public class VendorGroup
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CompanyId { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = "";

        public Guid? PayablesAccountId { get; set; }
    }
}