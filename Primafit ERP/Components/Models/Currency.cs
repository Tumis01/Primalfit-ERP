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

        public bool IsBaseCurrency { get; set; }
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

        // Filled from the selected target currency before saving. This legacy column
        // remains required in the database, but isn't entered directly on the form.
        public string ExchangeCurrency { get; set; } = string.Empty;

        public Guid? ExchangeCurrencyId { get; set; }

        [ForeignKey(nameof(ExchangeCurrencyId))]
        public Currency? ExchangeCurrencyRecord { get; set; }

        public DateTime Date { get; set; } 

        [Column(TypeName = "decimal(18,4)")]
        public decimal Rate { get; set; }
    }

}
