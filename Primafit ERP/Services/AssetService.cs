using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System.Text.Json;

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
            return await ctx.FixedAssets.AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.AssetTag).ToListAsync();
        }

        public async Task<string> CreateAssetAsync(FixedAsset asset)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(asset.AssetName)) return "Asset Name is required.";

            // Resolve Defaults from Category if fields are missing
            if (asset.AssetCategoryId.HasValue)
            {
                var cat = await ctx.AssetCategories.FindAsync(asset.AssetCategoryId.Value);
                if (cat != null)
                {
                    if (asset.FixedAssetAccountId == Guid.Empty && cat.FixedAssetAccountId.HasValue) asset.FixedAssetAccountId = cat.FixedAssetAccountId.Value;
                    if (asset.AccumulatedDepreciationAccountId == Guid.Empty && cat.AccumulatedDepreciationAccountId.HasValue) asset.AccumulatedDepreciationAccountId = cat.AccumulatedDepreciationAccountId.Value;
                    if (asset.DepreciationExpenseAccountId == Guid.Empty && cat.DepreciationExpenseAccountId.HasValue) asset.DepreciationExpenseAccountId = cat.DepreciationExpenseAccountId.Value;
                }
            }

            if (asset.FixedAssetAccountId == Guid.Empty || asset.AccumulatedDepreciationAccountId == Guid.Empty || asset.DepreciationExpenseAccountId == Guid.Empty)
                return "Please map all GL accounts (or select a category that has them configured).";

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
                    return "Cannot change Purchase Cost after depreciation has started.";

                asset.CompanyId = existing.CompanyId;
                asset.CurrentBookValue = existing.CurrentBookValue;
                asset.LastDepreciationDate = existing.LastDepreciationDate;
                ctx.Entry(existing).CurrentValues.SetValues(asset);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task RunAutomatedCatchUpForCompanyAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var assets = await ctx.FixedAssets.Where(a => a.CompanyId == companyId && a.Status == AssetStatus.Active).ToListAsync();
            if (!assets.Any()) return;

            DateTime? earliestLastRun = assets.Where(a => a.LastDepreciationDate.HasValue).Min(a => a.LastDepreciationDate);
            DateTime earliestStart = assets.Min(a => a.DepreciationStartDate);

            DateTime nextRunDate = earliestLastRun.HasValue
                ? new DateTime(earliestLastRun.Value.Year, earliestLastRun.Value.Month, 1).AddMonths(1).AddDays(DateTime.DaysInMonth(earliestLastRun.Value.Year, earliestLastRun.Value.AddMonths(1).Month) - 1)
                : new DateTime(earliestStart.Year, earliestStart.Month, DateTime.DaysInMonth(earliestStart.Year, earliestStart.Month));

            while (nextRunDate <= DateTime.Today)
            {
                await RunMonthlyDepreciationAsync(companyId, nextRunDate);
                var nextMonthStart = new DateTime(nextRunDate.Year, nextRunDate.Month, 1).AddMonths(1);
                nextRunDate = new DateTime(nextMonthStart.Year, nextMonthStart.Month, DateTime.DaysInMonth(nextMonthStart.Year, nextMonthStart.Month));
            }
        }
        public async Task<List<AssetDepreciationHistory>> GetAssetHistoryAsync(Guid assetId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.AssetDepreciationHistories
                .AsNoTracking()
                .Where(h => h.FixedAssetId == assetId)
                .OrderByDescending(h => h.Date) // Newest first
                .ToListAsync();
        }
        public async Task<string> RunMonthlyDepreciationAsync(Guid companyId, DateTime runDate)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Lock runDate to end of month
            runDate = new DateTime(runDate.Year, runDate.Month, DateTime.DaysInMonth(runDate.Year, runDate.Month));

            var assets = await ctx.FixedAssets
                .Where(a => a.CompanyId == companyId && a.Status == AssetStatus.Active && a.DepreciationStartDate <= runDate && a.CurrentBookValue > a.SalvageValue)
                .ToListAsync();

            var eligibleAssets = assets.Where(a =>
                a.LastDepreciationDate == null ||
                (a.LastDepreciationDate.Value.Year < runDate.Year) ||
                (a.LastDepreciationDate.Value.Year == runDate.Year && a.LastDepreciationDate.Value.Month < runDate.Month)
            ).ToList();

            if (!eligibleAssets.Any()) return "No eligible assets found.";

            // Prevent double-running via DB Check
            var existingHistoryForMonth = await ctx.AssetDepreciationHistories
                .Where(h => h.Date.Year == runDate.Year && h.Date.Month == runDate.Month && eligibleAssets.Select(a => a.Id).Contains(h.FixedAssetId))
                .Select(h => h.FixedAssetId)
                .ToListAsync();

            eligibleAssets.RemoveAll(a => existingHistoryForMonth.Contains(a.Id));
            if (!eligibleAssets.Any()) return "All eligible assets already processed for this period.";

            // Fetch Usage Logs for this month
            var usageLogs = await ctx.AssetUsageLogs
                .Where(u => u.CompanyId == companyId && u.PeriodDate.Year == runDate.Year && u.PeriodDate.Month == runDate.Month)
                .ToListAsync();

            var glLines = new List<GLJournalLine>();
            var newHistories = new List<AssetDepreciationHistory>();
            decimal totalRunAmount = 0;

            foreach (var asset in eligibleAssets)
            {
                decimal amount = 0;
                decimal? unitsUsedThisPeriod = null;

                // 1. ENGINE STRATEGY 
                switch (asset.DepreciationMethod)
                {
                    case DepreciationMethod.StraightLine:
                        if (asset.UsefulLifeMonths > 0)
                            amount = (asset.PurchaseCost - asset.SalvageValue) / asset.UsefulLifeMonths;
                        break;

                    case DepreciationMethod.ImmediateWriteOff:
                        amount = asset.CurrentBookValue - asset.SalvageValue;
                        break;

                    case DepreciationMethod.NoDepreciation:
                        amount = 0;
                        break;

                    case DepreciationMethod.DecliningBalance:
                        decimal annualRate = asset.DecliningRate ?? (asset.DecliningFactor.HasValue && asset.UsefulLifeMonths > 0 ? (asset.DecliningFactor.Value / (asset.UsefulLifeMonths / 12.0m)) : 0);
                        amount = (annualRate / 12.0m) * asset.CurrentBookValue;
                        break;

                    case DepreciationMethod.UnitsOfUsage:
                        var log = usageLogs.FirstOrDefault(u => u.FixedAssetId == asset.Id);
                        if (log != null && asset.EstimatedTotalUnits > 0)
                        {
                            unitsUsedThisPeriod = log.UnitsUsed;
                            amount = ((asset.PurchaseCost - asset.SalvageValue) / asset.EstimatedTotalUnits.Value) * unitsUsedThisPeriod.Value;
                        }
                        break;

                    case DepreciationMethod.SumOfYearsDigits:
                        if (asset.UsefulLifeMonths > 0)
                        {
                            decimal n = asset.UsefulLifeMonths;
                            decimal syd = n * (n + 1) / 2.0m;

                            int monthsElapsed = ((runDate.Year - asset.DepreciationStartDate.Year) * 12) + runDate.Month - asset.DepreciationStartDate.Month;
                            monthsElapsed = Math.Max(0, Math.Min(monthsElapsed, asset.UsefulLifeMonths - 1)); // Cap it

                            decimal remainingLife = n - monthsElapsed;
                            amount = (remainingLife / syd) * (asset.PurchaseCost - asset.SalvageValue);
                        }
                        break;
                }

                // 2. FLOOR ENFORCEMENT
                if (amount > 0)
                {
                    if ((asset.CurrentBookValue - amount) < asset.SalvageValue)
                        amount = asset.CurrentBookValue - asset.SalvageValue;

                    // 3. APPLY DEPRECIATION
                    if (amount > 0)
                    {
                        asset.CurrentBookValue -= amount;
                        asset.LastDepreciationDate = runDate;
                        if (asset.CurrentBookValue <= asset.SalvageValue) asset.Status = AssetStatus.FullyDepreciated;

                        glLines.Add(new GLJournalLine { SegCoaId = asset.DepreciationExpenseAccountId, Debit = amount, Credit = 0, Reference = $"Depr {asset.DepreciationMethod} {runDate:MM/yy}: {asset.AssetTag}" });
                        glLines.Add(new GLJournalLine { SegCoaId = asset.AccumulatedDepreciationAccountId, Debit = 0, Credit = amount, Reference = $"Accum Depr: {asset.AssetTag}" });

                        newHistories.Add(new AssetDepreciationHistory
                        {
                            FixedAssetId = asset.Id,
                            Date = runDate,
                            Amount = amount,
                            MethodUsed = asset.DepreciationMethod,
                            UnitsUsed = unitsUsedThisPeriod,
                            DetailsJson = JsonSerializer.Serialize(new { asset.PurchaseCost, asset.CurrentBookValue, amount })
                        });

                        totalRunAmount += amount;
                    }
                }
            }

            if (!glLines.Any()) return "Calculated 0 depreciation for all assets.";

            // 4. GUARANTEE CONSISTENCY (Lines -> Batch -> History Update -> DB Save -> Post)
            var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                companyId, DateOnly.FromDateTime(runDate),
                "Asset Depreciation", $"Auto-Run: {runDate:MMM yyyy}", glLines);

            if (!batchId.HasValue) return $"Failed to create GL Batch: {err}";

            foreach (var h in newHistories) h.GlBatchId = batchId.Value;
            ctx.AssetDepreciationHistories.AddRange(newHistories);

            await ctx.SaveChangesAsync(); // Assets and History saved WITH Batch ID

            // Final Post
            var postErr = await _glOps.PostBatchAsync(companyId, batchId.Value);
            if (!string.IsNullOrEmpty(postErr)) return $"Depreciation saved, but GL posting failed: {postErr}";

            return $"Successfully processed {newHistories.Count} assets. Total: {totalRunAmount:C}";
        }
    }
}