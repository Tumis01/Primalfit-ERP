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
    public class CustomerStatementReport
    {
        public string ReportName { get; set; } = "Customer Statement";
        public string ReportingPeriod { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string CompanyAddress { get; set; } = "";
        public string CompanyEmail { get; set; } = "";
        public string CompanyRegNo { get; set; } = "";
        public string GeneratedBy { get; set; } = "";
        public DateTime DateGenerated { get; set; } = DateTime.Now;
        public List<CustomerStatementLine> Lines { get; set; } = new();
    }

    public class CustomerStatementLine
    {
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = "";
        public string CustomerAddress { get; set; } = "";
        public DateOnly? Date { get; set; }
        public string DocumentNumber { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
        public bool IsBalanceRow { get; set; }
    }
    public class VendorStatementReport
    {
        public string ReportName { get; set; } = "Vendor Statement";
        public string ReportingPeriod { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string CompanyAddress { get; set; } = "";
        public string CompanyEmail { get; set; } = "";
        public string CompanyRegNo { get; set; } = "";
        public string GeneratedBy { get; set; } = "";
        public DateTime DateGenerated { get; set; } = DateTime.Now;
        public List<VendorStatementLine> Lines { get; set; } = new();
    }

    public class VendorStatementLine
    {
        public Guid VendorId { get; set; }
        public string VendorName { get; set; } = "";
        public string VendorAddress { get; set; } = "";
        public DateOnly? Date { get; set; }
        public string DocumentNumber { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
        public bool IsBalanceRow { get; set; }
    }

}