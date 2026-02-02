using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class UserApiService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public UserApiService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<List<UserDisplayDto>> GetUsersAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1. Fetch Users
            var users = await userManager.Users.ToListAsync();
            var displayList = new List<UserDisplayDto>();

            // 2. Fetch Company Lookup (Optimization to avoid N+1 query problem)
            var companies = await db.CompanyDetails
                                    .AsNoTracking()
                                    .ToDictionaryAsync(c => c.CompanyDetailsId, c => c.CompanyName);

            foreach (var user in users)
            {
                // Get Roles
                var roles = await userManager.GetRolesAsync(user);
                var roleName = roles.FirstOrDefault() ?? "No Role";

                // Get Company Name
                var companyName = "System Level"; // Default text
                if (user.CompanyDetailsId.HasValue && companies.ContainsKey(user.CompanyDetailsId.Value))
                {
                    companyName = companies[user.CompanyDetailsId.Value];
                }

                displayList.Add(new UserDisplayDto
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Email = user.Email,
                    Role = roleName,
                    CompanyName = companyName
                });
            }

            return displayList;
        }

        public async Task<bool> CreateUserAsync(UserDto model)
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Check if user exists
            if (await userManager.FindByEmailAsync(model.Email) != null)
            {
                return false; // User already exists
            }

            var newUser = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                CompanyDetailsId = model.CompanyDetailsId,
                EmailConfirmed = true // Auto-confirm for internal admins
            };

            // 1. Create User
            var result = await userManager.CreateAsync(newUser, model.Password);

            if (result.Succeeded)
            {
                // 2. Assign Role
                if (!string.IsNullOrEmpty(model.RoleName))
                {
                    await userManager.AddToRoleAsync(newUser, model.RoleName);
                }
                return true;
            }

            return false; // Failed (e.g., weak password)
        }

    }
}