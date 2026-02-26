namespace Primafit_ERP.Components.Models.Reporting
{
   
    public class StandardReportData
    {
        public string ReportName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = "Primalfit Group";
        public string Currency { get; set; } = "Base";
        public string ReportingPeriod { get; set; } = string.Empty;
        public string GeneratedBy { get; set; } = "System Automator";
        public DateTime DateGenerated { get; set; } = DateTime.Now;

        
        public List<string> Headers { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
    }
}