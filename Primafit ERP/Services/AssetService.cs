using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class AssetService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;

        public AssetService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
        }

        public async Task<List<FixedAsset>> GetAssetsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.FixedAssets
                .AsNoTracking()
                .Where(a => a.CompanyId == companyId)
                .OrderBy(a => a.AssetTag)
                .ToListAsync();
        }

        public async Task<string> CreateAssetAsync(FixedAsset asset)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(asset.AssetName)) return "Asset Name is required.";
            
            if (asset.FixedAssetAccountId == Guid.Empty || 
                asset.AccumulatedDepreciationAccountId == Guid.Empty || 
                asset.DepreciationExpenseAccountId == Guid.Empty)
            {
                return "Please map all GL accounts.";
            }

            var existing = await ctx.FixedAssets.FindAsync(asset.Id);

            if (existing == null)
            {
                if (asset.Id == Guid.Empty) asset.Id = Guid.NewGuid();
                asset.CurrentBookValue = asset.PurchaseCost; 
                ctx.FixedAssets.Add(asset);
            }
            else
            {
                if (existing.LastDepreciationDate.HasValue && existing.PurchaseCost != asset.PurchaseCost)
                {
                    return "Cannot change Purchase Cost after depreciation has started.";
                }
                
                asset.CompanyId = existing.CompanyId;
                asset.CurrentBookValue = existing.CurrentBookValue;
                asset.LastDepreciationDate = existing.LastDepreciationDate;
                
                ctx.Entry(existing).CurrentValues.SetValues(asset);
            }

            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        // =========================================================
        // AUTOMATED CATCH-UP LOGIC
        // =========================================================
        public async Task RunAutomatedCatchUpForCompanyAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Find the starting point
            var assets = await ctx.FixedAssets
                .Where(a => a.CompanyId == companyId && a.Status == AssetStatus.Active)
                .ToListAsync();

            if (!assets.Any()) return;

            DateTime? earliestLastRun = assets.Where(a => a.LastDepreciationDate.HasValue).Min(a => a.LastDepreciationDate);
            DateTime earliestStart = assets.Min(a => a.DepreciationStartDate);

            DateTime nextRunDate;

            if (earliestLastRun.HasValue)
            {
                // Start from the end of the month FOLLOWING the last run
                var last = earliestLastRun.Value;
                var nextMonthStart = new DateTime(last.Year, last.Month, 1).AddMonths(1);
                nextRunDate = new DateTime(nextMonthStart.Year, nextMonthStart.Month, DateTime.DaysInMonth(nextMonthStart.Year, nextMonthStart.Month));
            }
            else
            {
                // Start from end of the earliest start month
                nextRunDate = new DateTime(earliestStart.Year, earliestStart.Month, DateTime.DaysInMonth(earliestStart.Year, earliestStart.Month));
            }

            // 2. Loop until caught up to Today
            while (nextRunDate <= DateTime.Today)
            {
                await RunMonthlyDepreciationAsync(companyId, nextRunDate);

                // Move to end of next month
                var nextMonth = new DateTime(nextRunDate.Year, nextRunDate.Month, 1).AddMonths(1);
                nextRunDate = new DateTime(nextMonth.Year, nextMonth.Month, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            }
        }

        public async Task<string> RunMonthlyDepreciationAsync(Guid companyId, DateTime runDate)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var assets = await ctx.FixedAssets
                .Where(a => a.CompanyId == companyId
                            && a.Status == AssetStatus.Active
                            && a.DepreciationStartDate <= runDate
                            && a.CurrentBookValue > a.SalvageValue)
                .ToListAsync();

            // Filter assets already run for this specific period
            var eligibleAssets = assets.Where(a => 
                a.LastDepreciationDate == null || 
                (a.LastDepreciationDate.Value.Year < runDate.Year) || 
                (a.LastDepreciationDate.Value.Year == runDate.Year && a.LastDepreciationDate.Value.Month < runDate.Month)
            ).ToList();

            if (!eligibleAssets.Any()) return "No eligible assets found.";

            var glLines = new List<GLJournalLine>();
            decimal totalRunAmount = 0;

            foreach (var asset in eligibleAssets)
            {
                // Sequential Safety Check
                if (asset.LastDepreciationDate.HasValue)
                {
                    var expectedNext = asset.LastDepreciationDate.Value.AddMonths(1);
                    if (runDate > expectedNext.AddDays(5)) continue; 
                }

                if (asset.UsefulLifeMonths <= 0) continue;

                decimal monthlyAmount = (asset.PurchaseCost - asset.SalvageValue) / asset.UsefulLifeMonths;

                if ((asset.CurrentBookValue - monthlyAmount) < asset.SalvageValue)
                    monthlyAmount = asset.CurrentBookValue - asset.SalvageValue;

                if (monthlyAmount <= 0) continue;

                asset.CurrentBookValue -= monthlyAmount;
                asset.LastDepreciationDate = runDate;
                if (asset.CurrentBookValue <= asset.SalvageValue) asset.Status = AssetStatus.FullyDepreciated;

                glLines.Add(new GLJournalLine { SegCoaId = asset.DepreciationExpenseAccountId, Debit = monthlyAmount, Credit = 0, Reference = $"Depr {runDate:MM/yy}: {asset.AssetTag}" });
                glLines.Add(new GLJournalLine { SegCoaId = asset.AccumulatedDepreciationAccountId, Debit = 0, Credit = monthlyAmount, Reference = $"Accum Depr: {asset.AssetTag}" });

                ctx.Add(new AssetDepreciationHistory { FixedAssetId = asset.Id, Date = runDate, Amount = monthlyAmount });
                totalRunAmount += monthlyAmount;
            }

            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    companyId, DateOnly.FromDateTime(runDate), 
                    "Asset Depreciation", $"Auto-Run: {runDate:MMM yyyy}", glLines);
                
                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value);
            }

            await ctx.SaveChangesAsync();
            return $"Processed {eligibleAssets.Count} assets. Total: {totalRunAmount:C}";
        }
    }
}