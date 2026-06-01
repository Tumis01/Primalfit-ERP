using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class EmployeeService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public EmployeeService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- EMPLOYEE CRUD ---
        public async Task<List<Employee>> GetEmployeesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Employees
                .Include(e => e.Branch)
                .Include(e => e.JobRole).ThenInclude(j => j!.Department)
                .Include(e => e.SalaryStructure)
                .Where(e => e.CompanyId == companyId)
                .OrderByDescending(e => e.DateJoined)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Employee?> GetEmployeeByIdAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Employees
                .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);
        }

        public async Task<string> SaveEmployeeAsync(Employee employee)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            try
            {
                // Validate codes for duplicates
                bool codeExists = await ctx.Employees.AnyAsync(e =>
                    e.CompanyId == employee.CompanyId &&
                    e.EmployeeCode == employee.EmployeeCode &&
                    e.Id != employee.Id);

                if (codeExists) return "An employee with this Staff ID / Code already exists.";

                if (employee.Id == Guid.Empty)
                {
                    employee.Id = Guid.NewGuid();
                    ctx.Employees.Add(employee);
                }
                else
                {
                    ctx.Employees.Update(employee);
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Database Error: {ex.Message}";
            }
        }

        public async Task<string> DeleteEmployeeAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var emp = await ctx.Employees.FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

            if (emp == null) return "Employee not found.";

            // Prevent deletion if they have payroll records
            bool hasPayroll = await ctx.PayrollItems.AnyAsync(p => p.EmployeeId == id);
            if (hasPayroll) return "Cannot delete an employee who has existing payroll records. Suspend or Terminate them instead.";

            ctx.Employees.Remove(emp);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // --- LOOKUP DATA FOR THE FORM ---
        public async Task<List<Branch>> GetBranchesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Branches.Where(b => b.CompanyId == companyId).OrderBy(b => b.Name).AsNoTracking().ToListAsync();
        }

        public async Task<List<JobRole>> GetJobRolesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.JobRoles.Include(j => j.Department).Where(j => j.CompanyId == companyId).OrderBy(j => j.Department!.Name).ThenBy(j => j.Title).AsNoTracking().ToListAsync();
        }

        public async Task<List<EmployeeSalaryStructure>> GetSalaryStructuresAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalaryStructures.Where(s => s.CompanyId == companyId).AsNoTracking().ToListAsync();
        }
    }
}