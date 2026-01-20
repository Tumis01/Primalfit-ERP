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
        public CompanyStatus Status { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        // Stores the URL/Path in the database (e.g., "/uploads/logo-123.png")
        public string? LogoPath { get; set; }
        // Transports the file content from Blazor to API. 
        // [NotMapped] ensures EF Core ignores this and doesn't try to create a column for it.
        [NotMapped]
        public string? NewLogoBase64 { get; set; }
        // [NotMapped] to store the file extension (e.g., ".png") so we save it correctly
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
    Government = 5
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
