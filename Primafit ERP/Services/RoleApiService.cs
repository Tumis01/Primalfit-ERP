using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public class RoleApiService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public RoleApiService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<List<ApplicationRole>> GetRolesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            return await roleManager.Roles.ToListAsync();
        }

        public async Task<bool> CreateRoleAsync(ApplicationRole role)
        {
            using var scope = _scopeFactory.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            if (await roleManager.RoleExistsAsync(role.Name)) return false;

            var result = await roleManager.CreateAsync(role);
            return result.Succeeded;
        }

        public async Task DeleteRoleAsync(string roleId)
        {
            using var scope = _scopeFactory.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            var role = await roleManager.FindByIdAsync(roleId);
            if (role != null)
            {
                await roleManager.DeleteAsync(role);
            }
        }
    }
}