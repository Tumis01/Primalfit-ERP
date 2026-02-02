using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class Currency
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public string CurrencyName { get; set; }

        [Required]
        public string CurrencyCode { get; set; }
    }

    public class CurrencyManagement
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid CurrencyId { get; set; }

        [ForeignKey(nameof(CurrencyId))]
        public Currency? Currency { get; set; }

        [Required]
        public string ExchangeCurrency { get; set; } // base currency code (e.g. NGN)

        public DateTime Date { get; set; } 

        [Column(TypeName = "decimal(18,6)")]
        public decimal Rate { get; set; }
    }

}
