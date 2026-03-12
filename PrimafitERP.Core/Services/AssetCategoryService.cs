using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class AssetCategoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public AssetCategoryService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<AssetCategory>> GetCategoriesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.AssetCategories.AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync();
        }

        public async Task<string> SaveCategoryAsync(AssetCategory category)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (string.IsNullOrWhiteSpace(category.Name)) return "Category name is required.";

            // Check if this ID actually exists in the database
            var exists = await ctx.AssetCategories.AnyAsync(c => c.Id == category.Id);

            if (!exists)
            {
                // If it's empty, generate a new one. If it has a generated Guid but isn't in the DB, just Add it.
                if (category.Id == Guid.Empty) category.Id = Guid.NewGuid();
                ctx.AssetCategories.Add(category);
            }
            else
            {
                ctx.AssetCategories.Update(category);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task LogUsageAsync(Guid companyId, Guid assetId, DateTime periodDate, decimal units)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var eomDate = new DateTime(periodDate.Year, periodDate.Month, DateTime.DaysInMonth(periodDate.Year, periodDate.Month));

            var existing = await ctx.AssetUsageLogs.FirstOrDefaultAsync(u => u.FixedAssetId == assetId && u.PeriodDate.Year == eomDate.Year && u.PeriodDate.Month == eomDate.Month);

            if (existing != null) existing.UnitsUsed = units;
            else ctx.AssetUsageLogs.Add(new AssetUsageLog { CompanyId = companyId, FixedAssetId = assetId, PeriodDate = eomDate, UnitsUsed = units });

            await ctx.SaveChangesAsync();
        }
    }
}