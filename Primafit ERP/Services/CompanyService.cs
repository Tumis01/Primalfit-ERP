using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
namespace Primafit_ERP.Services
{
    public class CompanyService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        // We use IDbContextFactory because Blazor Server creates long-lived circuits
        public CompanyService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<CompanyDetails>> GetAllCompaniesAsync()
        {
            using var context = _factory.CreateDbContext();
            return await context.CompanyDetails.ToListAsync();
        }

        public async Task AddCompanyAsync(CompanyDetails company)
        {
            using var context = _factory.CreateDbContext();
            company.CreatedDate = DateTime.Now;
            company.ModifiedDate = DateTime.Now;
            context.CompanyDetails.Add(company);
            await context.SaveChangesAsync();
        }

        public async Task UpdateCompanyAsync(CompanyDetails company)
        {
            using var context = _factory.CreateDbContext();

            // We fetch the existing entity to ensure we are updating the correct one
            var existing = await context.CompanyDetails.FindAsync(company.CompanyDetailsId);
            if (existing != null)
            {
                // Update properties
                context.Entry(existing).CurrentValues.SetValues(company);
                existing.ModifiedDate = DateTime.Now;
                await context.SaveChangesAsync();
            }
        }

        public async Task DeleteCompanyAsync(int id)
        {
            using var context = _factory.CreateDbContext();
            var company = await context.CompanyDetails.FindAsync(id);
            if (company != null)
            {
                context.CompanyDetails.Remove(company);
                await context.SaveChangesAsync();
            }
        }
    }
}