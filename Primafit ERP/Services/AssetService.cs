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

        // 1. GET ASSETS
        public async Task<List<FixedAsset>> GetAssetsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.FixedAssets
                .AsNoTracking()
                .Where(a => a.CompanyId == companyId)
                .OrderBy(a => a.AssetTag)
                .ToListAsync();
        }

        // 2. ACQUIRE ASSET (Create or Update)
        public async Task<string> CreateAssetAsync(FixedAsset asset)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Validate Accounts
            if (asset.FixedAssetAccountId == Guid.Empty ||
                asset.AccumulatedDepreciationAccountId == Guid.Empty ||
                asset.DepreciationExpenseAccountId == Guid.Empty)
            {
                return "Please map all GL accounts (Asset, Accum. Depr, Expense).";
            }

            // 2. Check Database for existence
            bool exists = await ctx.FixedAssets.AnyAsync(a => a.Id == asset.Id);

            if (!exists)
            {
                // --- NEW ASSET ---
                if (asset.Id == Guid.Empty) asset.Id = Guid.NewGuid();
                // Set Initial Book Value
                asset.CurrentBookValue = asset.PurchaseCost;
                ctx.FixedAssets.Add(asset);
            }
            else
            {
                // --- UPDATE EXISTING ---
                ctx.FixedAssets.Update(asset);
            }

            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Error saving asset: {ex.Message}";
            }
        }

        // 3. RUN DEPRECIATION (The Core Engine)
        public async Task<string> RunMonthlyDepreciationAsync(Guid companyId, DateTime periodDate)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // A. Find Assets eligible for depreciation
            var assets = await ctx.FixedAssets
                .Where(a => a.CompanyId == companyId
                            && a.Status == AssetStatus.Active
                            && a.DepreciationStartDate <= periodDate
                            && a.CurrentBookValue > a.SalvageValue)
                .ToListAsync();

            if (!assets.Any()) return "No eligible assets found for depreciation.";

            // Filter out assets already depreciated this month
            var eligibleAssets = assets.Where(a =>
                a.LastDepreciationDate == null ||
                (a.LastDepreciationDate.Value.Month != periodDate.Month || a.LastDepreciationDate.Value.Year != periodDate.Year)
            ).ToList();

            if (!eligibleAssets.Any()) return "Depreciation already run for this month.";

            var glLines = new List<GLJournalLine>();
            decimal totalDepreciation = 0;

            // B. Calculate for each asset
            foreach (var asset in eligibleAssets)
            {
                if (asset.UsefulLifeMonths <= 0) continue;

                // Straight Line: (Cost - Salvage) / Life
                decimal monthlyAmount = (asset.PurchaseCost - asset.SalvageValue) / asset.UsefulLifeMonths;

                // Cap check: Don't depreciate below salvage
                if ((asset.CurrentBookValue - monthlyAmount) < asset.SalvageValue)
                {
                    monthlyAmount = asset.CurrentBookValue - asset.SalvageValue;
                }

                if (monthlyAmount <= 0) continue;

                // Update Asset
                asset.CurrentBookValue -= monthlyAmount;
                asset.LastDepreciationDate = periodDate;
                if (asset.CurrentBookValue <= asset.SalvageValue)
                {
                    asset.Status = AssetStatus.FullyDepreciated;
                }

                // Add to History
                ctx.Add(new AssetDepreciationHistory
                {
                    FixedAssetId = asset.Id,
                    Date = periodDate,
                    Amount = monthlyAmount
                });

                // Build GL Lines
                // Dr Depreciation Expense
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = asset.DepreciationExpenseAccountId, // CHANGED: AccountId -> SegCoaId
                    Debit = monthlyAmount,
                    Credit = 0,
                    Reference = $"Depr: {asset.AssetTag}"
                });

                // Cr Accumulated Depreciation
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = asset.AccumulatedDepreciationAccountId, // CHANGED: AccountId -> SegCoaId
                    Debit = 0,
                    Credit = monthlyAmount,
                    Reference = $"Accum Depr: {asset.AssetTag}"
                });

                totalDepreciation += monthlyAmount;
            }

            // C. Post Batch
            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    companyId,
                    DateOnly.FromDateTime(periodDate),
                    "Asset Depreciation",
                    $"Monthly Run: {periodDate:MMM yyyy}",
                    glLines
                );

                if (!string.IsNullOrEmpty(err)) return $"GL Error: {err}";

                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value);
            }

            await ctx.SaveChangesAsync();
            return $"Success! Depreciated {eligibleAssets.Count} assets. Total: {totalDepreciation:C}";
        }
    }
}