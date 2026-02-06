using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class ProjectService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public ProjectService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- CRUD OPERATIONS ---

        public async Task<List<Project>> GetProjectsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Projects
                .Where(p => p.CompanyId == companyId)
                .OrderByDescending(p => p.StartDate)
                .ToListAsync();
        }

        public async Task<Project?> GetProjectByIdAsync(Guid projectId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Projects.FindAsync(projectId);
        }

        public async Task<string> SaveProjectAsync(Project project)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            try
            {
                if (project.Id == Guid.Empty)
                {
                    project.Id = Guid.NewGuid();
                    ctx.Projects.Add(project);
                }
                else
                {
                    ctx.Projects.Update(project);
                }

                await ctx.SaveChangesAsync();
                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                return $"Error saving project: {ex.Message}";
            }
        }

        // --- REPORTING: PROJECT P&L (The ROI Engine) ---
        public async Task<List<GLTransaction>> GetProjectTransactionsAsync(Guid projectId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.GLTransactions
                .AsNoTracking() // Read-only for performance
                .Where(t => t.ProjectId == projectId)
                .OrderByDescending(t => t.PostingDate) // Newest first
                .Take(100) // Limit to last 100 entries to keep UI fast
                .ToListAsync();
        }
        public async Task<ProjectPLViewModel> GetProjectPLAsync(Guid projectId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch all GL Transactions tagged to this Project
            var txns = await ctx.GLTransactions
                .AsNoTracking()
                .Where(t => t.ProjectId == projectId)
                .ToListAsync();

            if (!txns.Any()) return new ProjectPLViewModel { ProjectId = projectId };

            // 2. Identify Account Types (Revenue vs Expense)
            // We need to look up the Account Class for every transaction found.
            var accountIds = txns.Select(t => t.AccountId).Distinct().ToList();

            var accountTypes = await ctx.GLChartOfAccounts
                .Include(a => a.MainAccount.AccountType)
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.MainAccount.AccountType.Class);

            // 3. Aggregate Data
            var model = new ProjectPLViewModel { ProjectId = projectId };

            foreach (var t in txns)
            {
                if (accountTypes.TryGetValue(t.AccountId, out var type))
                {
                    // Logic: Project P&L only cares about Income and Expenses

                    if (type == GLAccountClass.Revenue)
                    {
                        // Revenue is normally Credit. 
                        // Net Impact = Credit - Debit (e.g., Sales - Returns)
                        model.ActualRevenue += (t.Credit - t.Debit);
                    }
                    else if (type == GLAccountClass.Expenses)
                    {
                        // Expense is normally Debit.
                        // Net Impact = Debit - Credit (e.g., Cost - Refunds)
                        model.ActualCost += (t.Debit - t.Credit);
                    }
                }
            }

            return model;
        }
    }

    // ViewModel for the Report
    public class ProjectPLViewModel
    {
        public Guid ProjectId { get; set; }
        public decimal ActualRevenue { get; set; }
        public decimal ActualCost { get; set; }

        public decimal Margin => ActualRevenue - ActualCost;

        public decimal MarginPercent => ActualRevenue != 0
            ? (Margin / ActualRevenue) * 100
            : 0;
    }
}