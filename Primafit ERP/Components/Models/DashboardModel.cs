namespace Primafit_ERP.Components.Models
{
    public class FinancialDashboardSnapshot
    {
        public Guid CompanyId { get; set; }
        public DateTime LastRefresh { get; set; } = DateTime.Now;

        // Pulse Metrics
        public decimal CashOnHand { get; set; }
        public decimal CurrentRatio { get; set; }
        public decimal QuickRatio { get; set; }
        public decimal RevenueMTD { get; set; }
        public decimal NetProfitMTD { get; set; }
        public decimal GrossProfitMargin { get; set; }
        public decimal NetProfitMargin { get; set; }
        public decimal DaysSalesOutstanding { get; set; }
        public decimal InventoryTurnover { get; set; }

        // Action Center Counts
        public int LowStockItemsCount { get; set; }
        public int OverdueInvoicesCount { get; set; }
        public int UnshippedOrdersCount { get; set; }
        public int PendingTransfersCount { get; set; }
        public int APExceptionsCount { get; set; }

        // --- NEW: CHART DATA MODELS ---
        public List<MonthlyTrend> SixMonthCashFlow { get; set; } = new();
        public AgingProfile ARAging { get; set; } = new();
        public AgingProfile APAging { get; set; } = new();
        public List<ExpenseDistribution> TopExpenses { get; set; } = new();
        public decimal RevenueYTD { get; set; }
        public decimal AROutstandingTotal { get; set; }
        public decimal APOutstandingTotal { get; set; }
        public decimal InventoryValue { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal PreviousMonthRevenue { get; set; }
        public decimal PreviousMonthProfit { get; set; }
        public List<TopCustomerSummary> TopCustomers { get; set; } = new();
        public List<UnshippedOrderSummary> UnshippedOrderDetails { get; set; } = new();
        public List<APExceptionSummary> APExceptionDetails { get; set; } = new();
        public List<LowStockSummary> LowStockDetails { get; set; } = new();
    }

    public class MonthlyTrend
    {
        public string Month { get; set; } = "";
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal NetProfit => Revenue - Expenses;
    }

    public class AgingProfile
    {
        public decimal Current { get; set; }
        public decimal Days1To30 { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal DaysOver60 { get; set; }
    }

    public class ExpenseDistribution
    {
        public string AccountName { get; set; } = "";
        public decimal Amount { get; set; }
    }
    public class TopCustomerSummary
    {
        public string CustomerName { get; set; } = string.Empty;
        public decimal RevenueMTD { get; set; }
        public int InvoiceCount { get; set; }
    }

    public class UnshippedOrderSummary
    {
        public Guid OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public decimal OrderValue { get; set; }
        public int DaysOld { get; set; }
    }

    public class APExceptionSummary
    {
        public string BillReference { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public decimal BillAmount { get; set; }
        public string VarianceReason { get; set; } = string.Empty;
    }

    public class LowStockSummary
    {
        public string ItemName { get; set; } = string.Empty;
        public string SKU { get; set; } = string.Empty;
        public decimal CurrentQty { get; set; }
        public decimal ReorderLevel { get; set; }
    }
}