using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using Microsoft.AspNetCore.Hosting; 

namespace Primafit_ERP.Services
{
    public class CompanyApiService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IWebHostEnvironment _environment;

        
        public CompanyApiService(IDbContextFactory<AppDbContext> dbFactory, IWebHostEnvironment environment)
        {
            _dbFactory = dbFactory;
            _environment = environment;
        }

        public async Task<List<CompanyDetails>> GetCompaniesAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.CompanyDetails
                                .Include(x => x.CreatedByUser) 
                                .AsNoTracking()
                                .OrderByDescending(x => x.CompanyDetailsId)
                                .ToListAsync();
        }

        public async Task<CompanyDetails?> GetCompanyByIdAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();

            return await context.CompanyDetails
                                .Include(x => x.CreatedByUser) 
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

            
            if (!string.IsNullOrEmpty(model.NewLogoBase64))
            {
                model.LogoPath = await SaveLogoFileAsync(model.NewLogoBase64, model.NewLogoExtension ?? ".png");
            }

            context.CompanyDetails.Add(model);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateCompanyAsync(CompanyDetails model)
        {
            using var context = _dbFactory.CreateDbContext();

            var entity = await context.CompanyDetails.FindAsync(model.CompanyDetailsId);
            if (entity == null) return false;

            // --- LOGO UPDATE LOGIC ---
            if (!string.IsNullOrEmpty(model.NewLogoBase64))
            {
                
                entity.LogoPath = await SaveLogoFileAsync(model.NewLogoBase64, model.NewLogoExtension ?? ".png");
            }

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

        // save Base64 to Disk
        private async Task<string> SaveLogoFileAsync(string base64Data, string extension)
        {
            try
            {
                
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "logos");

                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                
                if (base64Data.Contains(","))
                {
                    base64Data = base64Data.Split(',')[1];
                }

                var imageBytes = Convert.FromBase64String(base64Data);
                await File.WriteAllBytesAsync(filePath, imageBytes);

                
                return $"/uploads/logos/{fileName}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logo Upload Failed: {ex.Message}");
                return null;
            }
        }
    }
}