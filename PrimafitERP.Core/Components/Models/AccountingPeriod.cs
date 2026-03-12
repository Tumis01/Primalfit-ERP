using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class AccountingPeriod
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string PeriodName { get; set; } 

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; } 

        public bool IsClosed { get; set; } = false; 

       
        public Guid CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public virtual CompanyDetails? Company { get; set; }
    }
}