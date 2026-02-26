using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class BusinessPartner
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [Required] public Guid CompanyId { get; set; }

        [Required] public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? TaxId { get; set; }
        public bool IsCustomer { get; set; }
        public bool IsVendor { get; set; }
        public Guid? ReceivablesAccountId { get; set; }
        public Guid? PayablesAccountId { get; set; }

        [ForeignKey(nameof(ReceivablesAccountId))] 
        public GLChartOfAccount? ReceivablesAccount { get; set; }
        [ForeignKey(nameof(PayablesAccountId))] 
        public GLChartOfAccount? PayablesAccount { get; set; }
    }
}