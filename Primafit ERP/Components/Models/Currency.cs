using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class Currency
    {
        [Key]
        public Guid Id { get; set; }
        public string CurrencyName { get; set; }
        public string? CurrencyCode { get; set; }
    }
    public class CurrencyManagement
    {
        public Guid Id { get; set; }
        public Guid CurrencyId { get; set; }
        public Currency CurrencyName { get; set; }
        public string ExchangeCurrency { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public decimal Rate { get; set; }
    }
}
