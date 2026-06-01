using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    public class CompanyEmailSetting
    {
        [Key]
        public Guid CompanyId { get; set; }
        public bool EnableInvoiceEmailPopup { get; set; } = true; // Default to showing it
    }
}
