using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

            // 1. Check for existing email globally before doing anything
            if (await userManager.FindByEmailAsync(model.Email) != null)
                return (false, "This email is already registered.");

            CompanyDetails? newCompany = null;

            try
            {
                // 2. Create Company 
                // Note: We leave BaseCurrency and FiscalStartYear out so they default to null. 
                // This ensures the forced onboarding prompt triggers on first login!
                newCompany = new CompanyDetails
                {
                    CompanyDetailsId = Guid.NewGuid(),
                    CompanyName = model.CompanyName,
                    CompanyEmail = model.CompanyEmail,
                    Type = model.Type,
                    CreatedDate = DateTime.UtcNow
                };

                db.CompanyDetails.Add(newCompany);
                await db.SaveChangesAsync();

                // 3. Create Super Admin Role for this specific company
                var superAdminRole = new ApplicationRole("SuperAdmin", newCompany.CompanyDetailsId, "Full System Access");
                var roleResult = await roleManager.CreateAsync(superAdminRole);

                if (!roleResult.Succeeded)
                {
                    // Manual Rollback: Delete the company we just created if role fails
                    db.CompanyDetails.Remove(newCompany);
                    await db.SaveChangesAsync();
                    return (false, "Failed to create Admin Role: " + string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }

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
                if (!userResult.Succeeded)
                {
                    // Manual Rollback: Delete the role and company if user creation fails
                    await roleManager.DeleteAsync(superAdminRole);
                    db.CompanyDetails.Remove(newCompany);
                    await db.SaveChangesAsync();
                    return (false, "Failed to create User: " + string.Join(", ", userResult.Errors.Select(e => e.Description)));
                }

                // 5. Assign Role
                var assignResult = await userManager.AddToRoleAsync(adminUser, superAdminRole.Name);
                if (!assignResult.Succeeded)
                {
                    // Final Rollback attempt if role assignment fails
                    await userManager.DeleteAsync(adminUser);
                    await roleManager.DeleteAsync(superAdminRole);
                    db.CompanyDetails.Remove(newCompany);
                    await db.SaveChangesAsync();
                    return (false, "Failed to assign role to user.");
                }

                return (true, "Registration successful!");
            }
            catch (Exception ex)
            {
                // Grab the deepest Inner Exception to see the ACTUAL SQL error
                string exactError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;

                // Cleanup: If it failed mid-way, try to delete the orphaned company record
                if (newCompany != null && newCompany.CompanyDetailsId != Guid.Empty)
                {
                    var existingComp = await db.CompanyDetails.FindAsync(newCompany.CompanyDetailsId);
                    if (existingComp != null)
                    {
                        db.CompanyDetails.Remove(existingComp);
                        await db.SaveChangesAsync();
                    }
                }

                // Return the exact SQL error to the UI
                return (false, $"DB Error: {exactError}");
            }
        }

        public async Task<(bool Success, string Message)> LoginAsync(LoginDto model)
        {
            using var scope = _scopeFactory.CreateScope();
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByEmailAsync(model.Email);
            if (user == null) return (false, "Invalid credentials.");

            var result = await signInManager.PasswordSignInAsync(user, model.Password, isPersistent: true, lockoutOnFailure: false);

            if (result.Succeeded) return (true, string.Empty);
            if (result.IsLockedOut) return (false, "Account is locked out.");

            return (false, "Invalid credentials.");
        }
    }
}