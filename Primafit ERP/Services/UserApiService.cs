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

        // 1. GET USERS (Scoped to Company)
        public async Task<List<UserDisplayDto>> GetUsersAsync(Guid companyId)
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            // Filter users by CompanyId directly
            var users = await userManager.Users
                                .Where(u => u.CompanyDetailsId == companyId)
                                .ToListAsync();

            var displayList = new List<UserDisplayDto>();

            foreach (var user in users)
            {
                var roleNames = await userManager.GetRolesAsync(user);
                var systemRoleName = roleNames.FirstOrDefault();
                string displayRole = "No Role";

                // Convert "System Role" (Guid_Manager) to "Display Role" (Manager)
                if (systemRoleName != null)
                {
                    var role = await roleManager.FindByNameAsync(systemRoleName);
                    if (role != null) displayRole = role.DisplayName;
                }

                displayList.Add(new UserDisplayDto
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Email = user.Email,
                    Role = displayRole,
                    //IsActive = true // Can map from LockoutEnabled if needed
                });
            }

            return displayList;
        }

        // 2. CREATE USER (Internal Flow)
        public async Task<string> CreateUserAsync(UserDto model)
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            // A. Validation
            if (model.CompanyDetailsId == Guid.Empty) return "System Error: Company Context is missing.";
            if (await userManager.FindByEmailAsync(model.Email) != null) return "User with this email already exists.";

            // B. Create User Object
            var newUser = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                CompanyDetailsId = model.CompanyDetailsId,
                EmailConfirmed = true // Internal creation implies trust
            };

            // C. Save to DB
            var result = await userManager.CreateAsync(newUser, model.Password);

            if (!result.Succeeded)
                return string.Join(", ", result.Errors.Select(e => e.Description));

            // D. Assign Role
            if (!string.IsNullOrEmpty(model.RoleDisplayName))
            {
                // Find the correct System Role for this Company
                // e.g. Find role where Name == "{CompanyId}_{RoleName}"
                var targetRoleName = $"{model.CompanyDetailsId}_{model.RoleDisplayName}".Replace(" ", "");

                // Double check it exists
                if (await roleManager.RoleExistsAsync(targetRoleName))
                {
                    await userManager.AddToRoleAsync(newUser, targetRoleName);
                }
                else
                {
                    return "User created, but Role could not be assigned (Role not found for this company).";
                }
            }

            return string.Empty; // Success
        }
    }
}