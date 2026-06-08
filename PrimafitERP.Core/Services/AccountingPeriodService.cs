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

            var company = await context.CompanyDetails
                .FirstOrDefaultAsync(c => c.CompanyDetailsId == companyId);

            if (company == null)
                throw new InvalidOperationException("Company not found. Check that the selected ID is the CompanyDetails primary key.");

            if (company.FiscalStartYear == null || company.FiscalEndYear == null)
                throw new InvalidOperationException("Fiscal dates are missing. Please complete the company setup first.");

            DateOnly fiscalStart = company.FiscalStartYear.Value;
            DateOnly fiscalEnd = company.FiscalEndYear.Value;

            if (fiscalEnd <= fiscalStart)
                throw new InvalidOperationException($"Invalid fiscal dates. Fiscal end ({fiscalEnd:yyyy-MM-dd}) must be after fiscal start ({fiscalStart:yyyy-MM-dd}).");

            // 1. Fetch existing periods, but DO NOT delete them!
            var existingPeriods = await context.AccountingPeriods
                .Where(p => p.CompanyId == companyId)
                .ToListAsync();

            var periodsToAdd = new List<AccountingPeriod>();

            // 2. Normalize the iterator to strictly align with calendar months
            var iterator = new DateOnly(fiscalStart.Year, fiscalStart.Month, 1);
            var endLimit = new DateOnly(fiscalEnd.Year, fiscalEnd.Month, DateTime.DaysInMonth(fiscalEnd.Year, fiscalEnd.Month));

            while (iterator <= endLimit)
            {
                var endOfThisPeriod = new DateOnly(iterator.Year, iterator.Month, DateTime.DaysInMonth(iterator.Year, iterator.Month));

                // 3. SAFE CHECK: Does this specific month/year already exist in the database?
                bool periodExists = existingPeriods.Any(p =>
                    p.StartDate.Year == iterator.Year && p.StartDate.Month == iterator.Month);

                if (!periodExists)
                {
                    periodsToAdd.Add(new AccountingPeriod
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        PeriodName = iterator.ToString("MMM yyyy"), // e.g. "Jan 2026" - Much safer than "Period 1"
                        StartDate = iterator,
                        EndDate = endOfThisPeriod,
                        IsClosed = false // New periods are open by default
                    });
                }

                // Move to the next month
                iterator = iterator.AddMonths(1);
            }

            // 4. Only hit the database if there are actually new periods to add
            if (periodsToAdd.Any())
            {
                context.AccountingPeriods.AddRange(periodsToAdd);
                await context.SaveChangesAsync();
            }

            return true;
        }
    }
}
