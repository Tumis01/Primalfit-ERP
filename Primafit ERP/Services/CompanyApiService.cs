using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class CompanyApiService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public CompanyApiService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<CompanyDetails>> GetCompaniesAsync()
        {
            using var context = _dbFactory.CreateDbContext();

            return await context.CompanyDetails
                                .AsNoTracking() // Faster for read-only lists
                                .OrderByDescending(x => x.CompanyDetailsId)
                                .ToListAsync();
        }

        public async Task<CompanyDetails?> GetCompanyByIdAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();

            return await context.CompanyDetails
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x => x.CompanyDetailsId == id);
        }

        public async Task<bool> CreateCompanyAsync(CompanyDetails model)
        {
            using var context = _dbFactory.CreateDbContext();

            if (model.CompanyDetailsId == Guid.Empty)
                model.CompanyDetailsId = Guid.NewGuid();

            model.CreatedDate = DateTime.UtcNow;
            model.ModifiedDate = DateTime.UtcNow;

            // Handle Logo Logic here if you aren't using a Controller anymore
            // (If complex file handling is needed, simple DB save is easiest here)

            context.CompanyDetails.Add(model);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateCompanyAsync(CompanyDetails model)
        {
            using var context = _dbFactory.CreateDbContext();

            var entity = await context.CompanyDetails.FindAsync(model.CompanyDetailsId);
            if (entity == null) return false;

            // Map fields manually to ensure safety
            entity.CompanyName = model.CompanyName;
            entity.ComanyRegNumber = model.ComanyRegNumber;
            entity.TaxIdentidicationNum = model.TaxIdentidicationNum;
            entity.CompanyEmail = model.CompanyEmail;
            entity.PhysicalAddress = model.PhysicalAddress;
            entity.PostalAddress = model.PostalAddress;
            entity.FiscalStartYear = model.FiscalStartYear;
            entity.FiscalEndYear = model.FiscalEndYear;
            entity.country = model.country;
            entity.CompanyWebsite = model.CompanyWebsite;
            entity.FunctionalCurrency = model.FunctionalCurrency;
            entity.BaseCurrency = model.BaseCurrency;
            entity.Type = model.Type;
            entity.Status = model.Status;

            // Logo path logic would go here if needed
            if (!string.IsNullOrEmpty(model.LogoPath))
            {
                entity.LogoPath = model.LogoPath;
            }

            entity.ModifiedDate = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteCompanyAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();

            var entity = await context.CompanyDetails.FindAsync(id);
            if (entity == null) return false;

            context.CompanyDetails.Remove(entity);
            await context.SaveChangesAsync();
            return true;
        }
    }
}