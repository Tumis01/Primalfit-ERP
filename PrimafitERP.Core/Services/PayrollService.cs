using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PayrollService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ComplianceService _compliance;

        public PayrollService(IDbContextFactory<AppDbContext> dbFactory, ComplianceService compliance)
        {
            _dbFactory = dbFactory;
            _compliance = compliance;
        }

        public async Task<List<PayrollRun>> GetPayrollRunsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.PayrollRuns
                .Where(p => p.CompanyId == companyId)
                .OrderByDescending(p => p.RunDate)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<PayrollRun?> GetPayrollRunDetailsAsync(Guid runId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.PayrollRuns
                .Include(r => r.PayrollItems)
                    .ThenInclude(i => i.Employee)
                .Include(r => r.PayrollItems)
                    .ThenInclude(i => i.CustomEarnings)
                .Include(r => r.PayrollItems)
                    .ThenInclude(i => i.CustomDeductions)
                .FirstOrDefaultAsync(r => r.Id == runId && r.CompanyId == companyId);
        }

        public async Task<string> GeneratePayrollRunAsync(Guid companyId, string period)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (await ctx.PayrollRuns.AnyAsync(p => p.CompanyId == companyId && p.Period == period))
                return "A payroll run for this period already exists.";

            var settings = await ctx.PayrollSettings.FirstOrDefaultAsync(s => s.CompanyId == companyId);
            if (settings == null) return "Missing Payroll Settings. Configure statutory rates first.";

            var employees = await ctx.Employees
                .Include(e => e.SalaryStructure)
                .Where(e => e.CompanyId == companyId && e.Status == EmploymentStatus.Active)
                .ToListAsync();

            if (!employees.Any()) return "No active employees found to run payroll.";

            var run = new PayrollRun
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Period = period,
                Status = PayrollRunStatus.Draft,
                RunDate = DateTime.UtcNow
            };

            foreach (var emp in employees)
            {
                var salary = emp.SalaryStructure!;
                decimal grossMonthly = salary.GrossMonthlyPay;

                var taxes = _compliance.CalculateNigeriaTaxes(
                    grossMonthly * 12,
                    settings.PensionEmployeeRate,
                    settings.PensionEmployerRate,
                    salary.IsPensionable);

                decimal paye = salary.IsTaxable ? taxes.PAYE : 0;
                decimal net = grossMonthly - paye - taxes.PensionEmp;

                run.PayrollItems.Add(new PayrollItem
                {
                    Id = Guid.NewGuid(),
                    PayrollRunId = run.Id,
                    EmployeeId = emp.Id,
                    GrossPay = grossMonthly,
                    PAYETax = paye,
                    EmployeePension = taxes.PensionEmp,
                    EmployerPension = taxes.PensionEmplr,
                    OtherEarnings = 0,
                    OtherDeductions = 0,
                    NetPay = net
                });
            }

            RecalculateRunTotals(run);
            ctx.PayrollRuns.Add(run);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> AddCustomEarningOrDeductionAsync(Guid payrollItemId, bool isEarning, string description, decimal amount)
        {
            if (amount <= 0) return "Amount must be greater than zero.";

            using var ctx = await _dbFactory.CreateDbContextAsync();
            var item = await ctx.PayrollItems
                .Include(i => i.PayrollRun)
                .Include(i => i.CustomEarnings)
                .Include(i => i.CustomDeductions)
                .FirstOrDefaultAsync(i => i.Id == payrollItemId);

            if (item == null) return "Payroll item not found.";
            if (item.PayrollRun?.Status != PayrollRunStatus.Draft) return "Cannot modify a locked payroll.";

            if (isEarning)
            {
                item.CustomEarnings.Add(new PayrollEarning { Id = Guid.NewGuid(), Description = description, Amount = amount });
                item.OtherEarnings += amount;
            }
            else
            {
                item.CustomDeductions.Add(new PayrollDeduction { Id = Guid.NewGuid(), Description = description, Amount = amount });
                item.OtherDeductions += amount;
            }

            // Recalculate item Net Pay
            item.NetPay = (item.GrossPay + item.OtherEarnings) - (item.PAYETax + item.EmployeePension + item.OtherDeductions);

            // Recalculate run totals
            RecalculateRunTotals(item.PayrollRun!);

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        private void RecalculateRunTotals(PayrollRun run)
        {
            run.TotalGrossPay = run.PayrollItems.Sum(i => i.GrossPay + i.OtherEarnings);
            run.TotalDeductions = run.PayrollItems.Sum(i => i.PAYETax + i.EmployeePension + i.OtherDeductions);
            run.TotalNetPay = run.PayrollItems.Sum(i => i.NetPay);
            run.TotalEmployerPension = run.PayrollItems.Sum(i => i.EmployerPension);
        }
    }
}