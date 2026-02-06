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

            // Validate Header
            if (string.IsNullOrWhiteSpace(budget.BudgetName)) return "Budget Name is required.";

            // 1. Check if the Budget actually exists in the DB
            var existing = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == budget.Id);

            if (existing == null)
            {
                // --- CASE 1: NEW BUDGET ---
                // Even if it has an ID, if it's not in the DB, we ADD it.
                ctx.BudgetHeaders.Add(budget);

                // Ensure lines are linked
                foreach (var line in budget.Lines)
                {
                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                    // EF will handle the Foreign Key automatically if added via the parent
                }
            }
            else
            {
                // --- CASE 2: UPDATE EXISTING ---
                // Update Header values
                ctx.Entry(existing).CurrentValues.SetValues(budget);

                // Replace Lines (Delete old, Insert new)
                ctx.BudgetLines.RemoveRange(existing.Lines);

                foreach (var line in budget.Lines)
                {
                    // Reset Line ID to ensure it inserts as new
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
        public async Task<string> ValidateFundsAsync(Guid companyId, List<(Guid GlAccountId, decimal Amount)> requests)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Check if there is an ACTIVE budget for this company
            var activeBudget = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);

            // If no active budget, we assume no controls (Pass)
            if (activeBudget == null) return string.Empty;

            foreach (var req in requests)
            {
                // Find the budget line for this specific GL Account
                var budgetLine = activeBudget.Lines.FirstOrDefault(l => l.GlAccountId == req.GlAccountId);

                // If account not listed in budget, we assume unlimited OR strictly 0 depending on policy.
                // Here we assume: If not in budget, NO restriction.
                if (budgetLine == null || budgetLine.LimitAmount == 0) continue;

                // A. Calculate Actuals (Sum of GL Debits for this account)
                // In a real app, filtering by Fiscal Year dates is crucial here.
                decimal actualSpent = await ctx.GLTransactions
                    .Where(t => t.CompanyId == companyId && t.AccountId == req.GlAccountId)
                    .SumAsync(t => t.Debit - t.Credit);

                // B. The Test
                decimal available = budgetLine.LimitAmount - actualSpent;

                if (req.Amount > available)
                {
                    return $"Budget Exceeded for Account. Limit: {budgetLine.LimitAmount:N0}, Used: {actualSpent:N0}, Requested: {req.Amount:N0}, Available: {available:N0}";
                }
            }

            return string.Empty;
        }
    }
}