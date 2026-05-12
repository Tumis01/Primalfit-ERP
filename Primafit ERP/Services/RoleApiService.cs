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
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Roles
                // FIX: Fetch Company specific roles OR Universal System roles (Guid.Empty)
                .Where(r => r.CompanyId == companyId || r.CompanyId == Guid.Empty)
                // Sort System Roles to the top, then alphabetically
                .OrderBy(r => r.CompanyId == Guid.Empty ? 0 : 1)
                .ThenBy(r => r.DisplayName)
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

            // Prevent deletion of system roles
            if (role.CompanyId == Guid.Empty) return "System roles cannot be deleted.";

            var result = await _roleManager.DeleteAsync(role);
            return result.Succeeded ? string.Empty : string.Join(", ", result.Errors.Select(e => e.Description));
        }

        // --- NEW RBAC METHODS FOR THE UI ---

        public async Task<List<Permission>> GetPageLevelPermissionsAsync()
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            // Return only the main modules/pages (where ActionKey is null)
            return await context.Permissions
                .Where(p => p.ActionKey == null)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Permission>> GetPermissionsForRoleAsync(string roleId)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var role = await _roleManager.FindByIdAsync(roleId);

            if (role == null) return new List<Permission>();

            // Check if it is a System Role (Global)
            if (role.CompanyId == Guid.Empty)
            {
                return await context.SystemRoleTemplates
                    .Where(srt => srt.RoleName == role.DisplayName)
                    .SelectMany(srt => context.SystemRolePermissions
                        .Where(srp => srp.SystemRoleTemplateId == srt.Id)
                        .Select(srp => srp.Permission))
                    .AsNoTracking()
                    .ToListAsync();
            }

            // Otherwise, it is a Custom Company Role
            return await context.CompanyRolePermissions
                .Where(crp => crp.ApplicationRoleId == roleId)
                .Select(crp => crp.Permission)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<string> SaveRoleWithPermissionsAsync(ApplicationRole role, List<int> permissionIds)
        {
            // 1. Save or Update the Identity Role using the existing logic
            var error = await SaveRoleAsync(role);
            if (!string.IsNullOrEmpty(error)) return error;

            // 2. Map the chosen permissions in the junction table
            using var context = await _dbFactory.CreateDbContextAsync();

            // Clear old permissions first
            var existing = await context.CompanyRolePermissions.Where(crp => crp.ApplicationRoleId == role.Id).ToListAsync();
            if (existing.Any())
            {
                context.CompanyRolePermissions.RemoveRange(existing);
            }

            // Add new permissions
            if (permissionIds != null && permissionIds.Any())
            {
                var newPerms = permissionIds.Select(pid => new CompanyRolePermission
                {
                    ApplicationRoleId = role.Id,
                    PermissionId = pid
                });
                context.CompanyRolePermissions.AddRange(newPerms);
            }

            await context.SaveChangesAsync();
            return string.Empty;
        }
    }
}