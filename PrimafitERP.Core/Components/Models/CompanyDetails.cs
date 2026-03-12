using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class CompanyDetails
    {
        public Guid CompanyDetailsId { get; set; }
        public string CompanyName { get; set; }
        public string? ComanyRegNumber { get; set; }
        public string? TaxIdentidicationNum { get; set; }
        public string CompanyEmail { get; set; }
        public string? PhysicalAddress { get; set; }
        public string? PostalAddress { get; set; }
        public DateOnly FiscalStartYear { get; set; }
        public DateOnly FiscalEndYear { get;set; }
        public string country { get; set; }
        public string? CompanyWebsite { get; set; }
        public string FunctionalCurrency { get;set; }
        public string BaseCurrency { get; set; }
        public CompanyType Type { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string? CreatedByUserId { get; set; } 

        [ForeignKey("CreatedByUserId")]
        public virtual ApplicationUser? CreatedByUser { get; set; }
        public string? LogoPath { get; set; }
        [NotMapped]
        public string? NewLogoBase64 { get; set; }
        [NotMapped]
        public string? NewLogoExtension { get; set; }

    }
}
public enum CompanyType
{
    SoleProprietorship = 1,
    Partnership = 2,
    LimitedLiabilityCompany = 3,
    NonProfit = 4,
    Government = 5,
    Corporation = 6
}

public enum CompanyStatus
{
    Active = 1,
    Inactive = 2
}
//public enum ComapanyType
//{
//    Sol,
//    SME, 
//    Enterprise,
//    Group
//}
