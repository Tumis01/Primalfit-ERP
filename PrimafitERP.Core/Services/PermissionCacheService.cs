using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System.Threading;

namespace Primafit_ERP.Services
{
    public class PermissionCacheService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly UserManager<ApplicationUser> _userManager;

        private HashSet<string> _permissions = new();
        private bool _loaded = false;
        private bool _isSuperAdmin = false;

        // 1. Introduce a Semaphore to act as an asynchronous lock
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public PermissionCacheService(
            IDbContextFactory<AppDbContext> dbFactory,
            UserManager<ApplicationUser> userManager)
        {
            _dbFactory = dbFactory;
            _userManager = userManager;
        }

        public async Task LoadAsync(string userId, Guid companyId)
        {
            // Fast exit if already loaded
            if (_loaded) return;

            // 2. Lock the thread asynchronously
            await _semaphore.WaitAsync();
            try
            {
                // 3. Double-check in case another thread loaded it while we were waiting
                if (_loaded) return;

                using var context = await _dbFactory.CreateDbContextAsync();
                var user = await _userManager.FindByIdAsync(userId);

                if (user != null)
                {
                    var roleNames = await _userManager.GetRolesAsync(user);

                    // SuperAdmin Bypass Check
                    if (roleNames.Any(r => r.Contains("SuperAdmin")))
                    {
                        _isSuperAdmin = true;
                    }

                    // Load permissions from System Templates
                    var systemPerms = await context.UserSystemRoles
                        .Where(usr => usr.UserId == userId && usr.CompanyId == companyId)
                        .SelectMany(usr => context.SystemRolePermissions
                            .Where(srp => srp.SystemRoleTemplateId == usr.SystemRoleTemplateId)
                            .Select(srp => srp.Permission))
                        .Select(p => p.ActionKey ?? p.PageKey)
                        .ToListAsync();

                    // Load custom ApplicationRoles for the user
                    var customPerms = await context.Roles
                        .Where(r => roleNames.Contains(r.Name) && r.CompanyId == companyId)
                        .Join(context.CompanyRolePermissions,
                              role => role.Id,
                              crp => crp.ApplicationRoleId,
                              (role, crp) => crp.Permission)
                        .Select(p => p.ActionKey ?? p.PageKey)
                        .ToListAsync();

                    // Combine both lists into the HashSet for O(1) fast lookups
                    foreach (var perm in systemPerms.Concat(customPerms).Where(p => !string.IsNullOrEmpty(p)))
                    {
                        _permissions.Add(perm!);
                    }
                }

                _loaded = true;
            }
            finally
            {
                // 4. Always release the lock, even if an error occurs
                _semaphore.Release();
            }
        }

        public bool Has(string key) => _isSuperAdmin || _permissions.Contains(key);

        public void Invalidate()
        {
            _permissions.Clear();
            _isSuperAdmin = false;
            _loaded = false;
        }
    }
}