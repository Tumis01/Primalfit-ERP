namespace Primafit_ERP.Components.Models
{
    public class FinancialDashboardSnapshot
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public DateTime LastRefresh { get; set; } = DateTime.UtcNow;

        // --- Liquidity (Executive) ---
        public decimal CurrentRatio { get; set; }
        public decimal QuickRatio { get; set; }
        public decimal CashOnHand { get; set; }

        // --- Profitability (Executive / Sales) ---
        public decimal GrossProfitMargin { get; set; }
        public decimal NetProfitMargin { get; set; }
        public decimal RevenueMTD { get; set; }
        public decimal NetProfitMTD { get; set; }

        // --- Efficiency (Warehouse / Ops) ---
        public decimal DaysSalesOutstanding { get; set; } // DSO
        public decimal InventoryTurnover { get; set; }

        // --- Alerts ---
        public int LowStockItemsCount { get; set; }
        public int OverdueInvoicesCount { get; set; }
    }
}