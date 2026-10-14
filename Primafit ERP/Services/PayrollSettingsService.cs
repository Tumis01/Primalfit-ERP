using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PayrollSettingsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public PayrollSettingsService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<PayrollSetting> GetSettingsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var settings = await ctx.PayrollSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.CompanyId == companyId);

            // Return defaults if not configured yet
            if (settings == null)
            {
                return new PayrollSetting
                {
                    CompanyId = companyId,
                    PensionEmployeeRate = 0.08m, // 8% default
                    PensionEmployerRate = 0.10m  // 10% default
                };
            }

            return settings;
        }

        public async Task<string> SaveSettingsAsync(PayrollSetting settings)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            try
            {
                if (settings.Id == Guid.Empty)
                {
                    settings.Id = Guid.NewGuid();
                    ctx.PayrollSettings.Add(settings);
                }
                else
                {
                    var existing = await ctx.PayrollSettings.FirstOrDefaultAsync(x => x.Id == settings.Id && x.CompanyId == settings.CompanyId);
                    if (existing == null) return "These payroll settings no longer exist. Reload the page and try again.";
                    ctx.Entry(existing).CurrentValues.SetValues(settings);
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (DbUpdateConcurrencyException)
            {
                return "These payroll settings changed while you were editing them. Reload and try again.";
            }
            catch (Exception ex)
            {
                return $"Database Error: {ex.Message}";
            }
        }
    }
}
