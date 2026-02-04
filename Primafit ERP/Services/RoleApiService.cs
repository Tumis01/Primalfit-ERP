using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class RoleApiService
    {
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public RoleApiService(RoleManager<ApplicationRole> roleManager, IDbContextFactory<AppDbContext> dbFactory)
        {
            _roleManager = roleManager;
            _dbFactory = dbFactory;
        }

        public async Task<List<ApplicationRole>> GetRolesAsync(Guid companyId)
        {
            // We use EF Core directly for filtering because RoleManager doesn't natively support CompanyId filtering easily
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Roles
                .Where(r => r.CompanyId == companyId)
                .OrderBy(r => r.DisplayName)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<string> SaveRoleAsync(ApplicationRole role)
        {
            if (role.CompanyId == Guid.Empty) return "Company context is missing.";
            if (string.IsNullOrWhiteSpace(role.DisplayName)) return "Role Name is required.";

            // 1. Construct the Unique System Name
            string systemName = $"{role.CompanyId}_{role.DisplayName}".Replace(" ", "");

            // 2. Check if NEW or EDIT
            if (string.IsNullOrEmpty(role.Id))
            {
                // Create New
                role.Name = systemName; // Unique Constraint
                var result = await _roleManager.CreateAsync(role);
                return result.Succeeded ? string.Empty : string.Join(", ", result.Errors.Select(e => e.Description));
            }
            else
            {
                // Update Existing
                var existing = await _roleManager.FindByIdAsync(role.Id);
                if (existing == null) return "Role not found.";

                existing.DisplayName = role.DisplayName;
                existing.Description = role.Description;
                existing.Name = systemName; // Update system name in case DisplayName changed

                var result = await _roleManager.UpdateAsync(existing);
                return result.Succeeded ? string.Empty : string.Join(", ", result.Errors.Select(e => e.Description));
            }
        }

        public async Task<string> DeleteRoleAsync(string roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null) return "Role not found.";

            var result = await _roleManager.DeleteAsync(role);
            return result.Succeeded ? string.Empty : string.Join(", ", result.Errors.Select(e => e.Description));
        }
    }
}