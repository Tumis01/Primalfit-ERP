using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class AccountingPeriodService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public AccountingPeriodService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<AccountingPeriod>> GetPeriodsAsync()
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.AccountingPeriods
                .AsNoTracking()
                .Include(p => p.Company)
                .OrderBy(p => p.StartDate)
                .ToListAsync();
        }

        public async Task TogglePeriodStatusAsync(Guid periodId, bool isClosed)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var period = await context.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == periodId);
            if (period == null) return;

            period.IsClosed = isClosed;
            await context.SaveChangesAsync();
        }

        public async Task<bool> GeneratePeriodsForCompanyAsync(Guid companyId)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            // IMPORTANT: don't assume FindAsync works unless companyId is the PRIMARY KEY
            var company = await context.CompanyDetails
                .FirstOrDefaultAsync(c => c.CompanyDetailsId == companyId);

            if (company == null)
                throw new InvalidOperationException("Company not found. Check that the selected ID is the CompanyDetails primary key.");

            var fiscalStart = company.FiscalStartYear;
            var fiscalEnd = company.FiscalEndYear;

            if (fiscalEnd <= fiscalStart)
                throw new InvalidOperationException($"Invalid fiscal dates. Fiscal end ({fiscalEnd:yyyy-MM-dd}) must be after fiscal start ({fiscalStart:yyyy-MM-dd}).");

            // remove existing periods first
            var existing = await context.AccountingPeriods
                .Where(p => p.CompanyId == companyId)
                .ToListAsync();

            if (existing.Count > 0)
                context.AccountingPeriods.RemoveRange(existing);

            // Generate monthly periods with inclusive EndDate
            var periodsToAdd = new List<AccountingPeriod>();
            var iterator = fiscalStart;
            int periodCount = 1;

            while (iterator <= fiscalEnd)
            {
                var endOfThisPeriod = iterator.AddMonths(1).AddDays(-1);

                if (endOfThisPeriod > fiscalEnd)
                    endOfThisPeriod = fiscalEnd;

                periodsToAdd.Add(new AccountingPeriod
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    PeriodName = $"Period {periodCount}",
                    StartDate = iterator,
                    EndDate = endOfThisPeriod,
                    IsClosed = false
                });

                iterator = endOfThisPeriod.AddDays(1);
                periodCount++;
            }

            context.AccountingPeriods.AddRange(periodsToAdd);
            await context.SaveChangesAsync();

            return true;
        }
    }
}
