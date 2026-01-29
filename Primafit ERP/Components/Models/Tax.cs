namespace Primafit_ERP.Components.Models
{
    public class Tax
    {
        public Guid Id { get; set; }
        public string TaxName { get; set; }
        public string TaxCode { get; set; }
        public double Per { get; set; }
    }
}