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

            if (category.Id == Guid.Empty)
            {
                category.Id = Guid.NewGuid();
                ctx.AssetCategories.Add(category);
            }
            else
            {
                var existing = await ctx.AssetCategories.FirstOrDefaultAsync(c => c.Id == category.Id && c.CompanyId == category.CompanyId);
                if (existing == null) return "This asset category no longer exists. Reload the list and try again.";
                ctx.Entry(existing).CurrentValues.SetValues(category);
            }

            try { await ctx.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return "This asset category changed while you were editing it. Reload and try again."; }
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
        public async Task<string> DeleteCategoryAsync(Guid categoryId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var category = await ctx.AssetCategories.FindAsync(categoryId);
            if (category == null) return "Category not found.";

            // SAFETY CHECK: Prevent deleting categories that are currently assigned to assets
            bool isInUse = await ctx.FixedAssets.AnyAsync(a => a.AssetCategoryId == categoryId);
            if (isInUse)
            {
                return "STOP: Cannot delete this category because it is currently assigned to one or more registered assets. Reassign those assets first.";
            }

            try
            {
                ctx.AssetCategories.Remove(category);
                await ctx.SaveChangesAsync();
                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                return $"Database Error: {ex.Message}";
            }
        }
    }
}
