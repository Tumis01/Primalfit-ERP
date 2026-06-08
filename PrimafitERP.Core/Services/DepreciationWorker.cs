using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class DepreciationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DepreciationWorker> _logger;

        public DepreciationWorker(IServiceProvider serviceProvider, ILogger<DepreciationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Asset Depreciation Worker Started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessAllCompaniesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while running Auto-Depreciation.");
                }

                // Wait 24 Hours
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task ProcessAllCompaniesAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                var assetService = scope.ServiceProvider.GetRequiredService<AssetService>();

                using var ctx = await dbFactory.CreateDbContextAsync();
                var companyIds = await ctx.CompanyDetails.Select(c => c.CompanyDetailsId).ToListAsync();

                // Define a system user ID for automated background tasks
                string systemUserId = "SYSTEM_AUTO";

                foreach (var companyId in companyIds)
                {
                    // Passed the systemUserId here
                    await assetService.RunAutomatedCatchUpForCompanyAsync(companyId, systemUserId);
                }
            }
        }
    }
}