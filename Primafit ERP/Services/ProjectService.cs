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
                .AsNoTracking()
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

            if (string.IsNullOrWhiteSpace(project.Name)) return "Project Name is required.";
            if (project.CompanyId == Guid.Empty) return "Company ID is missing.";

            var existing = await ctx.Projects.FirstOrDefaultAsync(p => p.Id == project.Id);

            try
            {
                if (existing == null)
                {
                    if (project.Id == Guid.Empty) project.Id = Guid.NewGuid();
                    ctx.Projects.Add(project);
                }
                else
                {
                    project.CompanyId = existing.CompanyId; // Protect Data
                    ctx.Entry(existing).CurrentValues.SetValues(project);
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Error saving project: {ex.Message}";
            }
        }

        public async Task<List<GLTransaction>> GetProjectTransactionsAsync(Guid projectId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.GLTransactions
                .AsNoTracking()
                .Where(t => t.ProjectId == projectId)
                .OrderByDescending(t => t.PostingDate)
                .Take(100)
                .ToListAsync();
        }

        // --- REPORTING: PROJECT P&L (Corrected for Segmented COA) ---
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
            // Use SegCoaId to find the Account, then the AccountType
            var accountIds = txns.Select(t => t.SegCoaId).Distinct().ToList();

            var accounts = await ctx.SegChartOfAccounts
                .AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .ToListAsync();

            // 3. Get Account Type Definitions (to check IsBalanceSheet/IsDebit)
            var types = await ctx.Set<SegAccountType>().AsNoTracking().ToListAsync();

            // 4. Aggregate Data
            var model = new ProjectPLViewModel { ProjectId = projectId };

            foreach (var t in txns)
            {
                var acct = accounts.FirstOrDefault(a => a.Id == t.SegCoaId);
                if (acct == null) continue;

                var type = types.FirstOrDefault(x => x.Id == acct.SegAccountTypeId);
                if (type == null) continue;

                // LOGIC: Project P&L only cares about Income Statement items (IsBalanceSheet = false)
                if (type.IsBalanceSheet == false)
                {
                    if (type.IsDebit == false)
                    {
                        // INCOME ACCOUNTS (Credit Normal)
                        // Net Impact = Credit - Debit
                        model.ActualRevenue += (t.Credit - t.Debit);
                    }
                    else
                    {
                        // EXPENSE ACCOUNTS (Debit Normal)
                        // Net Impact = Debit - Credit
                        model.ActualCost += (t.Debit - t.Credit);
                    }
                }
            }

            return model;
        }
    }

    public class ProjectPLViewModel
    {
        public Guid ProjectId { get; set; }
        public decimal ActualRevenue { get; set; }
        public decimal ActualCost { get; set; }
        public decimal Margin => ActualRevenue - ActualCost;
        public decimal MarginPercent => ActualRevenue != 0 ? (Margin / ActualRevenue) * 100 : 0;
    }
}