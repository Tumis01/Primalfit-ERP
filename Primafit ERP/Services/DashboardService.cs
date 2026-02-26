using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class DashboardService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public DashboardService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<FinancialDashboardSnapshot> GetSnapshotAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var snapshot = new FinancialDashboardSnapshot { CompanyId = companyId };

            // 1. Fetch Account Balances (Grouped by Class/Type)
            // Note: In a real production app, you would query a 'TrialBalance' materialized view for speed.
            var accountBalances = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId)
                .GroupBy(t => t.SegCoaId)
                .Select(g => new { AccountId = g.Key, Balance = g.Sum(t => t.Debit - t.Credit) })
                .ToListAsync();

            var accounts = await ctx.GLChartOfAccounts
                .Include(a => a.MainAccount.AccountType)
                .Where(a => a.CompanyId == companyId)
                .ToListAsync();

            // Helper to get balance by Class/Type
            decimal GetBalance(Func<GLChartOfAccount, bool> predicate)
            {
                var targetIds = accounts.Where(predicate).Select(a => a.Id).ToHashSet();
                return accountBalances.Where(b => targetIds.Contains(b.AccountId)).Sum(b => b.Balance);
            }

            // --- 2. Calculate Liquidity ---
            // Assets are typically Debits (+), Liabilities are Credits (-)
            // We abs() liabilities because they are stored as negative credit balances in some systems, 
            // but for ratios we need the magnitude.
            var currentAssets = GetBalance(a => a.MainAccount.AccountType.Class == GLAccountClass.Asset && a.MainAccount.AccountType.Name.Contains("Current"));
            var currentLiabilities = Math.Abs(GetBalance(a => a.MainAccount.AccountType.Class == GLAccountClass.Liability && a.MainAccount.AccountType.Name.Contains("Current")));
            var inventory = GetBalance(a => a.MainAccount.AccountType.Name.Contains("Inventory"));
            var cash = GetBalance(a => a.MainAccount.AccountType.Name.Contains("Bank") || a.MainAccount.AccountType.Name.Contains("Cash"));
            var ar = GetBalance(a => a.MainAccount.AccountType.Name.Contains("Receivable"));

            snapshot.CashOnHand = cash;
            snapshot.CurrentRatio = currentLiabilities != 0 ? currentAssets / currentLiabilities : 0;
            snapshot.QuickRatio = currentLiabilities != 0 ? (cash + ar) / currentLiabilities : 0;

            // --- 3. Calculate Performance (MTD) ---
            // Fetch MTD Transactions specifically
            var mtdTxns = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId && t.PostingDate >= DateOnly.FromDateTime(startOfMonth))
                .ToListAsync();

            var revenueIds = accounts.Where(a => a.MainAccount.AccountType.Class == GLAccountClass.Revenue).Select(a => a.Id).ToList();
            var cogsIds = accounts.Where(a => a.MainAccount.AccountType.Name.Contains("Cost of Goods")).Select(a => a.Id).ToList();
            var expenseIds = accounts.Where(a => a.MainAccount.AccountType.Class == GLAccountClass.Expenses).Select(a => a.Id).ToList();

            // Revenue is Credit (-), so we flip sign for display if needed, or stick to absolute logic
            decimal revenue = Math.Abs(mtdTxns.Where(t => revenueIds.Contains(t.SegCoaId)).Sum(t => t.Credit - t.Debit));
            decimal cogs = mtdTxns.Where(t => cogsIds.Contains(t.SegCoaId)).Sum(t => t.Debit - t.Credit);
            decimal totalExpenses = mtdTxns.Where(t => expenseIds.Contains(t.SegCoaId)).Sum(t => t.Debit - t.Credit);

            snapshot.RevenueMTD = revenue;
            snapshot.NetProfitMTD = revenue - totalExpenses; // Simplified Net Profit
            snapshot.GrossProfitMargin = revenue != 0 ? ((revenue - cogs) / revenue) * 100 : 0;
            snapshot.NetProfitMargin = revenue != 0 ? ((revenue - totalExpenses) / revenue) * 100 : 0;

            // --- 4. Alerts & Efficiency ---
            // Low Stock (Placeholder logic - requires InventoryItem table)
            // snapshot.LowStockItemsCount = await ctx.Items.CountAsync(i => i.StockOnHand < i.ReorderPoint);
            snapshot.LowStockItemsCount = 12; // Demo Data
            snapshot.OverdueInvoicesCount = 5; // Demo Data

            // DSO Calculation (Simplified)
            // (Average AR / Credit Sales) * Days
            // Assuming 30 day period for this snapshot
            snapshot.DaysSalesOutstanding = revenue != 0 ? (ar / revenue) * 30 : 0;

            return snapshot;
        }
    }
}