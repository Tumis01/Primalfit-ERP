using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class AccountingPeriod
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string PeriodName { get; set; } // e.g. "Period 1"

        public DateOnly StartDate { get; set; } // Inherited from Company Start

        public DateOnly EndDate { get; set; }   // Start + 1 Month

        public bool IsClosed { get; set; } = false; // The Checkbox

        // Link to Company
        public Guid CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public virtual CompanyDetails? Company { get; set; }
    }
}