using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class ProjectService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public ProjectService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

        public async Task<List<Project>> GetProjectsAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Projects.AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.IsAccountingProject)
                .OrderBy(p => p.ProjectCode)
                .ToListAsync();
        }

        public async Task<List<Project>> GetActiveProjectsAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Projects.AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.IsAccountingProject && p.IsActive)
                .OrderBy(p => p.ProjectCode)
                .ToListAsync();
        }

        public async Task<Project?> GetProjectByIdAsync(Guid projectId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId && p.IsAccountingProject);
        }

        public async Task<string> SaveProjectAsync(Project project)
        {
            if (project.CompanyId == Guid.Empty) return "Company ID is missing.";
            project.ProjectCode = (project.ProjectCode ?? string.Empty).Trim();
            project.Name = (project.Name ?? string.Empty).Trim();
            if (project.ProjectCode.Length == 0) return "Project Code is required.";
            if (project.Name.Length == 0) return "Project Name is required.";

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var duplicate = await ctx.Projects.AnyAsync(p => p.CompanyId == project.CompanyId
                && p.IsAccountingProject && p.ProjectCode == project.ProjectCode && p.Id != project.Id);
            if (duplicate) return "A project with this code already exists.";

            try
            {
                if (project.Id == Guid.Empty)
                {
                    project.Id = Guid.NewGuid();
                    project.IsAccountingProject = true;
                    // These legacy columns remain in the database for existing
                    // installations; they are not part of the new project setup.
                    project.BudgetedRevenue = 0;
                    project.BudgetedCost = 0;
                    project.Status = ProjectStatus.Active;
                    project.StartDate = DateTime.Today;
                    project.EndDate = null;
                    ctx.Projects.Add(project);
                }
                else
                {
                    var existing = await ctx.Projects.FirstOrDefaultAsync(p => p.Id == project.Id
                        && p.CompanyId == project.CompanyId && p.IsAccountingProject);
                    if (existing == null) return "Project not found.";
                    existing.ProjectCode = project.ProjectCode;
                    existing.Name = project.Name;
                    existing.Description = project.Description?.Trim();
                    existing.IsActive = project.IsActive;
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (DbUpdateException)
            {
                return "The project could not be saved. Check that its code is unique.";
            }
        }

        public async Task<string> DeleteProjectAsync(Guid companyId, Guid projectId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var project = await ctx.Projects.FirstOrDefaultAsync(p =>
                p.Id == projectId && p.CompanyId == companyId && p.IsAccountingProject);
            if (project == null) return "Project not found.";

            var hasPostedReportActivity = await ctx.GLTransactions.AnyAsync(transaction =>
                transaction.CompanyId == companyId && transaction.ProjectId == projectId
                && ctx.GLBatches.Any(batch => batch.Id == transaction.BatchId
                    && batch.CompanyId == companyId && batch.Status == BatchStatus.Posted));
            if (hasPostedReportActivity)
                return "This project has posted report activity and cannot be deleted.";

            ctx.Projects.Remove(project);
            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (DbUpdateException)
            {
                return "This project is attached to transaction records and cannot be deleted. Deactivate it instead.";
            }
        }

        public async Task<ProjectReportViewModel?> GetProjectReportAsync(
            Guid companyId, Guid projectId, DateOnly fromDate, DateOnly toDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var project = await ctx.Projects.AsNoTracking().FirstOrDefaultAsync(p =>
                p.CompanyId == companyId && p.Id == projectId && p.IsAccountingProject);
            if (project == null) return null;

            var allToDate = await ctx.GLTransactions.AsNoTracking()
                .Include(t => t.SegCoa)
                .Where(t => t.CompanyId == companyId && t.ProjectId == projectId && t.PostingDate <= toDate
                    && ctx.GLBatches.Any(batch => batch.Id == t.BatchId && batch.CompanyId == companyId
                        && batch.Status == BatchStatus.Posted))
                .ToListAsync();
            var types = await ctx.Set<SegAccountType>().AsNoTracking().ToDictionaryAsync(x => x.Id);
            var report = new ProjectReportViewModel { Project = project, FromDate = fromDate, ToDate = toDate };

            report.Transactions = allToDate.Where(t => t.PostingDate >= fromDate)
                .OrderByDescending(t => t.PostingDate).ToList();

            report.ProfitAndLossAccounts = BuildAccountRows(
                allToDate.Where(t => t.PostingDate >= fromDate && t.SegCoa != null
                    && types.TryGetValue(t.SegCoa.SegAccountTypeId, out var type) && !type.IsBalanceSheet), types);

            report.BalanceSheetAccounts = BuildAccountRows(
                allToDate.Where(t => t.SegCoa != null
                    && types.TryGetValue(t.SegCoa.SegAccountTypeId, out var type) && type.IsBalanceSheet), types);
            return report;
        }

        private static List<ProjectAccountBalanceRow> BuildAccountRows(
            IEnumerable<GLTransaction> transactions, IReadOnlyDictionary<int, SegAccountType> types)
        {
            return transactions.Where(t => t.SegCoa != null)
                .GroupBy(t => t.SegCoa!)
                .Select(group =>
                {
                    var account = group.Key;
                    types.TryGetValue(account.SegAccountTypeId, out var type);
                    var debit = group.Sum(x => x.Debit);
                    var credit = group.Sum(x => x.Credit);
                    var debitNormal = type?.IsDebit ?? true;
                    return new ProjectAccountBalanceRow
                    {
                        AccountCode = account.AccountCode,
                        AccountName = account.Description,
                        AccountType = type?.Description ?? string.Empty,
                        Debit = debit,
                        Credit = credit,
                        Balance = debitNormal ? debit - credit : credit - debit,
                        IsDebitNormal = debitNormal
                    };
                })
                .OrderBy(x => x.AccountCode)
                .ToList();
        }
    }

    public class ProjectReportViewModel
    {
        public Project Project { get; set; } = new();
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        public List<ProjectAccountBalanceRow> ProfitAndLossAccounts { get; set; } = new();
        public List<ProjectAccountBalanceRow> BalanceSheetAccounts { get; set; } = new();
        public List<GLTransaction> Transactions { get; set; } = new();
        public decimal Revenue => ProfitAndLossAccounts.Where(x => !x.IsDebitNormal).Sum(x => x.Balance);
        public decimal Expenses => ProfitAndLossAccounts.Where(x => x.IsDebitNormal).Sum(x => x.Balance);
    }

    public class ProjectAccountBalanceRow
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
        public bool IsDebitNormal { get; set; }
    }
}
