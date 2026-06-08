using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class HrSetupService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public HrSetupService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- BRANCHES ---
        public async Task<List<Branch>> GetBranchesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Branches.Where(b => b.CompanyId == companyId).OrderBy(b => b.Name).AsNoTracking().ToListAsync();
        }

        public async Task<string> SaveBranchAsync(Branch branch)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (await ctx.Branches.AnyAsync(b => b.CompanyId == branch.CompanyId && b.Name.ToLower() == branch.Name.ToLower() && b.Id != branch.Id))
                return "A branch with this name already exists.";

            if (branch.Id == Guid.Empty) { branch.Id = Guid.NewGuid(); ctx.Branches.Add(branch); }
            else { ctx.Branches.Update(branch); }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // --- DEPARTMENTS ---
        public async Task<List<Department>> GetDepartmentsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Departments.Where(d => d.CompanyId == companyId).OrderBy(d => d.Name).AsNoTracking().ToListAsync();
        }

        public async Task<string> SaveDepartmentAsync(Department dept)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (await ctx.Departments.AnyAsync(d => d.CompanyId == dept.CompanyId && d.Name.ToLower() == dept.Name.ToLower() && d.Id != dept.Id))
                return "A department with this name already exists.";

            if (dept.Id == Guid.Empty) { dept.Id = Guid.NewGuid(); ctx.Departments.Add(dept); }
            else { ctx.Departments.Update(dept); }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // --- JOB ROLES ---
        public async Task<List<JobRole>> GetJobRolesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.JobRoles.Include(j => j.Department)
                .Where(j => j.CompanyId == companyId).OrderBy(j => j.Department!.Name).ThenBy(j => j.Title).AsNoTracking().ToListAsync();
        }

        public async Task<string> SaveJobRoleAsync(JobRole role)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (await ctx.JobRoles.AnyAsync(r => r.CompanyId == role.CompanyId && r.DepartmentId == role.DepartmentId && r.Title.ToLower() == role.Title.ToLower() && r.Id != role.Id))
                return "This job role already exists in the selected department.";

            if (role.Id == Guid.Empty) { role.Id = Guid.NewGuid(); ctx.JobRoles.Add(role); }
            else { ctx.JobRoles.Update(role); }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // --- SALARY STRUCTURES (COMPENSATION BANDS) ---
        public async Task<List<EmployeeSalaryStructure>> GetSalaryStructuresAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalaryStructures.Where(s => s.CompanyId == companyId).OrderBy(s => s.BasicSalary).AsNoTracking().ToListAsync();
        }

        public async Task<string> SaveSalaryStructureAsync(EmployeeSalaryStructure structure)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (structure.Id == Guid.Empty) { structure.Id = Guid.NewGuid(); ctx.SalaryStructures.Add(structure); }
            else { ctx.SalaryStructures.Update(structure); }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        // Add these to HrSetupService.cs
        public async Task<string> DeleteBranchAsync(Guid id, Guid companyId)
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var entity = await ctx.Branches.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
                if (entity == null) return "Not found.";
                ctx.Branches.Remove(entity);
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch { return "Cannot delete this Branch. It is currently in use by an employee."; }
        }

        public async Task<string> DeleteDepartmentAsync(Guid id, Guid companyId)
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var entity = await ctx.Departments.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
                if (entity == null) return "Not found.";
                ctx.Departments.Remove(entity);
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch { return "Cannot delete this Department. It is currently in use."; }
        }

        public async Task<string> DeleteJobRoleAsync(Guid id, Guid companyId)
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var entity = await ctx.JobRoles.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
                if (entity == null) return "Not found.";
                ctx.JobRoles.Remove(entity);
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch { return "Cannot delete this Job Role. It is currently in use."; }
        }

        public async Task<string> DeleteSalaryStructureAsync(Guid id, Guid companyId)
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var entity = await ctx.SalaryStructures.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
                if (entity == null) return "Not found.";
                ctx.SalaryStructures.Remove(entity);
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch { return "Cannot delete this Salary Band. It is currently assigned to an employee."; }
        }
    }
}