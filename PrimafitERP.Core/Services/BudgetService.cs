using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class BudgetService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public BudgetService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }
        // Add these to BudgetService.cs
        public async Task<List<BudgetHeader>> GetAllBudgetsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BudgetHeaders
                .AsNoTracking()
                .Include(b => b.Lines) // Include to count lines/sum total
                .Where(b => b.CompanyId == companyId)
                .OrderByDescending(b => b.IsActive).ThenBy(b => b.BudgetName)
                .ToListAsync();
        }

        public async Task<BudgetHeader?> GetBudgetByIdAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);
        }

        // 1. SAVE BUDGET
        public async Task<string> SaveBudgetAsync(BudgetHeader budget)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(budget.BudgetName)) return "Budget Name is required.";

            var existing = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == budget.Id);

            if (existing == null)
            {
                if (budget.Id == Guid.Empty) budget.Id = Guid.NewGuid();
                foreach (var line in budget.Lines)
                {
                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                    line.BudgetHeaderId = budget.Id;
                }
                ctx.BudgetHeaders.Add(budget);
            }
            else
            {
                budget.CompanyId = existing.CompanyId; // Protect company ID
                ctx.Entry(existing).CurrentValues.SetValues(budget);
                ctx.BudgetLines.RemoveRange(existing.Lines);

                foreach (var line in budget.Lines)
                {
                    line.Id = Guid.NewGuid();
                    line.BudgetHeaderId = existing.Id;
                    ctx.BudgetLines.Add(line);
                }
            }

            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Error saving budget: {ex.Message}";
            }
        }

        // 2. CHECK FUNDS (The Core Logic)
        public async Task<string> ValidateFundsAsync(Guid companyId, List<(Guid SegCoaId, decimal Amount)> requests)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Get the Active Budget
            var activeBudget = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);

            // If no active budget exists, we assume spending is unrestricted.
            if (activeBudget == null) return string.Empty;

            foreach (var req in requests)
            {
                // 2. Identify the Budget Limit for this Account
                var budgetLine = activeBudget.Lines.FirstOrDefault(l => l.GlAccountId == req.SegCoaId);

                // If this specific account isn't budgeted (limit 0), we skip the check (allow spending).
                if (budgetLine == null || budgetLine.LimitAmount == 0) continue;

                // -------------------------------------------------------------
                // A. CALCULATE ACTUALS (Money already gone)
                // Sum of posted GL Transactions (Debits - Credits)
                // -------------------------------------------------------------
                decimal actualSpent = await ctx.GLTransactions
                    .Where(t => t.CompanyId == companyId && t.SegCoaId == req.SegCoaId)
                    .SumAsync(t => t.Debit - t.Credit);

                // -------------------------------------------------------------
                // B. CALCULATE ENCUMBRANCES (Money promised in POs)
                // We must find Open PO Lines that target this GL Account.
                // Since PO Lines link to Items, we join via the Item definition.
                // -------------------------------------------------------------

                // Fetch Item IDs that map to this GL Account (Inventory Asset or Expense Account)
                var relevantItemIds = await ctx.Items
                    .Where(i => i.CompanyId == companyId &&
                               (i.InventoryAssetAccountId == req.SegCoaId || i.CostOfGoodsSoldAccountId == req.SegCoaId))
                    .Select(i => i.Id)
                    .ToListAsync();

                decimal encumberedAmount = 0;

                if (relevantItemIds.Any())
                {
                    encumberedAmount = await ctx.PurchaseOrders
                        .Where(p => p.CompanyId == companyId
                                    && (p.Status == PurchaseOrderStatus.Open || p.Status == PurchaseOrderStatus.PartiallyReceived)
                                    && !p.IsInvoicePosted)
                        .SelectMany(p => p.Lines)
                        .Where(l => relevantItemIds.Contains(l.ItemId))
                        .SumAsync(l => l.QuantityOrdered * l.UnitCost);
                }

                // -------------------------------------------------------------
                // C. THE GATEKEEPER FORMULA
                // -------------------------------------------------------------
                decimal totalCommitted = actualSpent + encumberedAmount;
                decimal fundsAvailable = budgetLine.LimitAmount - totalCommitted;

                // Check if the NEW request fits
                if (req.Amount > fundsAvailable)
                {
                    // HARD STOP MESSAGE
                    return $@"BUDGET EXCEEDED for Account. 
                              Budget: {budgetLine.LimitAmount:N2} 
                              | Spent: {actualSpent:N2} 
                              | Committed(POs): {encumberedAmount:N2} 
                              | Available: {fundsAvailable:N2} 
                              | Requested: {req.Amount:N2}";
                }
            }

            return string.Empty; // All checks passed
        }
        
        public async Task<BudgetHeader?> GetActiveBudgetAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .AsNoTracking() 
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);
        }
        public async Task<StandardReportData> GenerateVarianceReportAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var activeBudget = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);

            var report = new StandardReportData
            {
                ReportName = "Budget Variance & Commitment Report",
                ReportingPeriod = $"Active Budget: {activeBudget?.BudgetName ?? "None"}",
                Headers = new List<string> { "Account Code", "Account Name", "Limit (Budget)", "Actuals (Posted)", "Encumbered (POs)", "Total Committed", "Available Balance", "% Used" }
            };

            if (activeBudget == null || !activeBudget.Lines.Any())
            {
                report.Rows.Add(new List<string> { "N/A", "No Active Budget found.", "", "", "", "", "", "" });
                return report;
            }

            decimal grandBudget = 0, grandActuals = 0, grandEncumbered = 0, grandCommitted = 0, grandAvailable = 0;

            foreach (var line in activeBudget.Lines)
            {
                var account = await ctx.SegChartOfAccounts.FindAsync(line.GlAccountId);
                if (account == null) continue;

                // 1. Calculate Actuals (Posted GL)
                decimal actualSpent = await ctx.GLTransactions
                    .Where(t => t.CompanyId == companyId && t.SegCoaId == line.GlAccountId)
                    .SumAsync(t => t.Debit - t.Credit);

                // 2. Calculate Encumbrances (Open POs)
                var relevantItemIds = await ctx.Items
                    .Where(i => i.CompanyId == companyId &&
                                (i.InventoryAssetAccountId == line.GlAccountId || i.CostOfGoodsSoldAccountId == line.GlAccountId))
                    .Select(i => i.Id)
                    .ToListAsync();

                decimal encumberedAmount = 0;
                if (relevantItemIds.Any())
                {
                    encumberedAmount = await ctx.PurchaseOrders
                        .Where(p => p.CompanyId == companyId
                                    && (p.Status == PurchaseOrderStatus.Open || p.Status == PurchaseOrderStatus.PartiallyReceived)
                                    && !p.IsInvoicePosted)
                        .SelectMany(p => p.Lines)
                        .Where(l => relevantItemIds.Contains(l.ItemId))
                        .SumAsync(l => l.QuantityOrdered * l.UnitCost);
                }

                // 3. Math
                decimal totalCommitted = actualSpent + encumberedAmount;
                decimal available = line.LimitAmount - totalCommitted;

                decimal percentUsed = 0;
                if (line.LimitAmount > 0) percentUsed = (totalCommitted / line.LimitAmount) * 100;

                // 4. Formatting
                report.Rows.Add(new List<string>
        {
            account.AccountCode,
            account.Description,
            line.LimitAmount.ToString("N2"),
            actualSpent.ToString("N2"),
            encumberedAmount.ToString("N2"),
            totalCommitted.ToString("N2"),
            available.ToString("N2"),
            $"{percentUsed:N1}%"
        });

                // Add to Grand Totals
                grandBudget += line.LimitAmount;
                grandActuals += actualSpent;
                grandEncumbered += encumberedAmount;
                grandCommitted += totalCommitted;
                grandAvailable += available;
            }

            decimal grandPercent = grandBudget > 0 ? (grandCommitted / grandBudget) * 100 : 0;

            report.Rows.Add(new List<string> { "", "", "", "", "", "", "", "" }); // Spacer
            report.Rows.Add(new List<string>
    {
        "TOTALS",
        "COMPANY WIDE",
        grandBudget.ToString("N2"),
        grandActuals.ToString("N2"),
        grandEncumbered.ToString("N2"),
        grandCommitted.ToString("N2"),
        grandAvailable.ToString("N2"),
        $"{grandPercent:N1}%"
    });

            return report;
        }
    }
}