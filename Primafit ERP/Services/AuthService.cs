using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class AuthService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public AuthService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<(bool Success, string Message)> RegisterTenantAsync(RegisterTenantDto model)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            
            
            // 1. Check for existing email globally
            if (await userManager.FindByEmailAsync(model.Email) != null)
                return (false, "This email is already registered.");

            // START TRANSACTION
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // 2. Create Company
                var newCompany = new CompanyDetails
                {
                    CompanyDetailsId = Guid.NewGuid(),
                    CompanyName = model.CompanyName,
                    CompanyEmail = model.CompanyEmail,
                    Type = model.Type, // <--- SAVED HERE
                    CreatedDate = DateTime.UtcNow,

                    // System Defaults
                    FiscalStartYear = DateOnly.FromDateTime(DateTime.Today),
                    FiscalEndYear = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                    BaseCurrency = "NGN",
                    FunctionalCurrency = "NGN",
                    country = "Nigeria",
                };
                db.CompanyDetails.Add(newCompany);
                await db.SaveChangesAsync();

                // 3. Create "SuperAdmin" Role for THIS Company
                // Note: The unique system name combines CompanyID + RoleName to allow multiple "SuperAdmins" in the DB
                var superAdminRole = new ApplicationRole("SuperAdmin", newCompany.CompanyDetailsId, "Full System Access");

                var roleResult = await roleManager.CreateAsync(superAdminRole);
                if (!roleResult.Succeeded) throw new Exception("Failed to create Admin Role: " + roleResult.Errors.First().Description);

                // 4. Create Admin User linked to Company
                var adminUser = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    CompanyDetailsId = newCompany.CompanyDetailsId, // LINKED HERE
                    EmailConfirmed = true,
                };

                var userResult = await userManager.CreateAsync(adminUser, model.Password);
                if (!userResult.Succeeded) throw new Exception("Failed to create User: " + userResult.Errors.First().Description);

                // 5. Assign Role
                await userManager.AddToRoleAsync(adminUser, superAdminRole.Name);

                // COMMIT
                await transaction.CommitAsync();
                return (true, "Registration successful!");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Registration failed: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> LoginAsync(LoginDto model)
        {
            using var scope = _scopeFactory.CreateScope();
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByEmailAsync(model.Email);
            if (user == null) return (false, "Invalid credentials.");

            // Optional: Check if Company is Active
            // var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // var company = await db.CompanyDetails.FindAsync(user.CompanyDetailsId);
            // if (company == null || !company.IsActive) return (false, "Company account is suspended.");

            var result = await signInManager.PasswordSignInAsync(user, model.Password, isPersistent: true, lockoutOnFailure: false);

            if (result.Succeeded) return (true, string.Empty);
            if (result.IsLockedOut) return (false, "Account is locked out.");

            return (false, "Invalid credentials.");
        }
    }
}