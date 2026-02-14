using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
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

            var activeBudget = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);

            if (activeBudget == null) return string.Empty;

            foreach (var req in requests)
            {
                // Find the budget line for this specific GL Account
                var budgetLine = activeBudget.Lines.FirstOrDefault(l => l.GlAccountId == req.SegCoaId);

                // If not in budget or limit is 0, assume unlimited/uncontrolled
                if (budgetLine == null || budgetLine.LimitAmount == 0) continue;

                // A. Calculate Actuals (Sum of GL Debits for this SegCoaId)
                // Note: Production systems should filter by Fiscal Year dates here
                decimal actualSpent = await ctx.GLTransactions
                    .Where(t => t.CompanyId == companyId && t.SegCoaId == req.SegCoaId) // CORRECTED
                    .SumAsync(t => t.Debit - t.Credit);

                // B. The Test
                decimal available = budgetLine.LimitAmount - actualSpent;

                if (req.Amount > available)
                {
                    return $"Budget Exceeded. Limit: {budgetLine.LimitAmount:N0}, Used: {actualSpent:N0}, Requested: {req.Amount:N0}, Available: {available:N0}";
                }
            }

            return string.Empty;
        }
    }
}