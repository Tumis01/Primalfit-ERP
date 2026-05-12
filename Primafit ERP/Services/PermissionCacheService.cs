using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Data;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PermissionCacheService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly UserManager<ApplicationUser> _userManager;

        private HashSet<string> _permissions = new();
        private bool _loaded = false;
        private bool _isSuperAdmin = false;

        public PermissionCacheService(
            IDbContextFactory<AppDbContext> dbFactory,
            UserManager<ApplicationUser> userManager)
        {
            _dbFactory = dbFactory;
            _userManager = userManager;
        }

        public async Task LoadAsync(string userId, Guid companyId)
        {
            if (_loaded) return;

            using var context = await _dbFactory.CreateDbContextAsync();
            var user = await _userManager.FindByIdAsync(userId);

            if (user != null)
            {
                var roleNames = await _userManager.GetRolesAsync(user);

                // 1. SuperAdmin Bypass Check
                if (roleNames.Any(r => r.Contains("SuperAdmin")))
                {
                    _isSuperAdmin = true;
                }

                // 2. Load permissions from System Templates (Track 1)
                var systemPerms = await context.UserSystemRoles
                    .Where(usr => usr.UserId == userId && usr.CompanyId == companyId)
                    .SelectMany(usr => context.SystemRolePermissions
                        .Where(srp => srp.SystemRoleTemplateId == usr.SystemRoleTemplateId)
                        .Select(srp => srp.Permission))
                    .Select(p => p.ActionKey ?? p.PageKey) // Prefer ActionKey if it exists, otherwise PageKey
                    .ToListAsync();

                // 3. Load custom ApplicationRoles for the user (Track 2)
                var customPerms = await context.Roles
                    .Where(r => roleNames.Contains(r.Name) && r.CompanyId == companyId)
                    .Join(context.CompanyRolePermissions,
                          role => role.Id,
                          crp => crp.ApplicationRoleId,
                          (role, crp) => crp.Permission)
                    .Select(p => p.ActionKey ?? p.PageKey)
                    .ToListAsync();

                // 4. Combine both lists into the HashSet for O(1) fast lookups
                foreach (var perm in systemPerms.Concat(customPerms).Where(p => !string.IsNullOrEmpty(p)))
                {
                    _permissions.Add(perm!);
                }
            }

            _loaded = true;
        }

        // Fast in-memory check used by the Guards and UI. 
        // If they are a SuperAdmin, this instantly returns true for EVERYTHING.
        public bool Has(string key) => _isSuperAdmin || _permissions.Contains(key);

        // Call this if a user's role is changed while they are logged in
        public void Invalidate()
        {
            _permissions.Clear();
            _isSuperAdmin = false;
            _loaded = false;
        }

    }
}