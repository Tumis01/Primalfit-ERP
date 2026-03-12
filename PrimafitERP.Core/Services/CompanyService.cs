//using Microsoft.EntityFrameworkCore;
//using Microsoft.AspNetCore.Hosting;
//using Primafit_ERP.Components.Models;
//using PrimafitERP.Data;

//namespace Primafit_ERP.Services
//{
//    public class CompanyService
//    {
//        private readonly IDbContextFactory<AppDbContext> _factory;
//        private readonly IWebHostEnvironment _environment; // access server folders

//        public CompanyService(IDbContextFactory<AppDbContext> factory, IWebHostEnvironment environment)
//        {
//            _factory = factory;
//            _environment = environment;
//        }

//        public async Task<List<CompanyDetails>> GetAllCompaniesAsync()
//        {
//            using var context = _factory.CreateDbContext();
//            return await context.CompanyDetails.ToListAsync();
//        }

//        public async Task AddCompanyAsync(CompanyDetails company)
//        {
//            using var context = _factory.CreateDbContext();

//            // Save Image to Disk BEFORE saving to DB
//            await SaveLogoToDisk(company);

//            company.CreatedDate = DateTime.Now;
//            company.ModifiedDate = DateTime.Now;

//            context.CompanyDetails.Add(company);
//            await context.SaveChangesAsync();
//        }

//        public async Task UpdateCompanyAsync(CompanyDetails company)
//        {
//            using var context = _factory.CreateDbContext();

//            var existing = await context.CompanyDetails.FindAsync(company.CompanyDetailsId);
//            if (existing != null)
//            {
//                // Check for new image
//                await SaveLogoToDisk(company);

//                // If no new logo, keep the old path
//                if (string.IsNullOrEmpty(company.LogoPath))
//                {
//                    company.LogoPath = existing.LogoPath;
//                }

//                // Update database values
//                context.Entry(existing).CurrentValues.SetValues(company);
//                existing.ModifiedDate = DateTime.Now;
//                await context.SaveChangesAsync();
//            }
//        }

//        public async Task DeleteCompanyAsync(int id)
//        {
//            using var context = _factory.CreateDbContext();
//            var company = await context.CompanyDetails.FindAsync(id);
//            if (company != null)
//            {
//                context.CompanyDetails.Remove(company);
//                await context.SaveChangesAsync();
//            }
//        }

//        // --- HELPER METHOD TO SAVE FILE ---
//        private async Task SaveLogoToDisk(CompanyDetails company)
//        {
//            // Only run if the user uploaded a new image (Base64 string is present)
//            if (!string.IsNullOrEmpty(company.NewLogoBase64))
//            {
//                // 1. Get the path to wwwroot/uploads/companies
//                var folderPath = Path.Combine(_environment.WebRootPath, "uploads", "companies");

//                // 2. Create folder if it doesn't exist
//                if (!Directory.Exists(folderPath))
//                {
//                    Directory.CreateDirectory(folderPath);
//                }

//                // 3. Create a unique filename
//                var fileName = $"company_{Guid.NewGuid()}{company.NewLogoExtension}";
//                var fullPath = Path.Combine(folderPath, fileName);

//                // 4. Convert the string back to bytes and write to disk
//                var imageBytes = Convert.FromBase64String(company.NewLogoBase64);
//                await File.WriteAllBytesAsync(fullPath, imageBytes);

//                // 5. Update the object with the public URL (this is what gets saved to SQL)
//                company.LogoPath = $"/uploads/companies/{fileName}";

//                // 6. Clear the heavy string to free memory
//                company.NewLogoBase64 = null;
//            }
//        }
//    }
//}