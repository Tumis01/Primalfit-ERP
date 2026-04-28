using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class CurrencyService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public CurrencyService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- CURRENCIES ---

        public async Task<List<Currency>> GetCurrenciesAsync(Guid companyId)
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.Currencies.AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .OrderBy(c => c.CurrencyName)
                .ToListAsync();
        }

        public async Task<string> SaveCurrencyAsync(Guid companyId, Currency currency)
        {
            using var context = _dbFactory.CreateDbContext();

            if (companyId == Guid.Empty) return "Select a company first.";

            var code = (currency.CurrencyCode ?? "").Trim().ToUpperInvariant();
            var name = (currency.CurrencyName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                return "Code and Name are required.";

            // Check duplicates
            bool exists = await context.Currencies.AnyAsync(c =>
                c.CompanyId == companyId &&
                (c.CurrencyCode == code || c.CurrencyName == name) &&
                c.Id != currency.Id);

            if (exists) return "Currency Code or Name already exists.";

            if (currency.Id == Guid.Empty)
            {
                currency.Id = Guid.NewGuid();
                currency.CompanyId = companyId;
                currency.CurrencyCode = code;
                currency.CurrencyName = name;
                context.Currencies.Add(currency);
            }
            else
            {
                var existing = await context.Currencies.FindAsync(currency.Id);
                if (existing == null) return "Not found.";
                existing.CurrencyCode = code;
                existing.CurrencyName = name;
            }

            await context.SaveChangesAsync();
            return string.Empty;
        }

        // Added Delete for Currency
        public async Task<string> DeleteCurrencyAsync(Guid companyId, Guid currencyId)
        {
            using var context = _dbFactory.CreateDbContext();

            // Check if used in rates
            bool inUse = await context.CurrencyManagements.AnyAsync(r => r.CurrencyId == currencyId);
            if (inUse) return "Cannot delete: This currency has exchange rates recorded.";

            var item = await context.Currencies.FirstOrDefaultAsync(c => c.Id == currencyId && c.CompanyId == companyId);
            if (item != null)
            {
                context.Currencies.Remove(item);
                await context.SaveChangesAsync();
                return string.Empty; // Success
            }
            return "Currency not found.";
        }

        // --- RATES ---

        public async Task<List<CurrencyManagement>> GetRatesAsync(Guid companyId)
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.CurrencyManagements.AsNoTracking()
                .Include(r => r.Currency)
                .Where(r => r.CompanyId == companyId)
                .OrderByDescending(r => r.Date)
                .ToListAsync();
        }

        public async Task<string> SaveRateAsync(Guid companyId, CurrencyManagement rate, string baseCurrency)
        {
            using var context = await _dbFactory.CreateDbContextAsync(); // Use Async version

            if (companyId == Guid.Empty) return "Select a company first.";
            if (rate.CurrencyId == Guid.Empty) return "Select a Currency.";
            if (rate.Rate <= 0) return "Rate must be greater than 0.";
            if (string.IsNullOrWhiteSpace(baseCurrency)) return "Company Base Currency is not set.";

            // --- THE FIX ---
            // 1. Strip time to ensure 00:00:00
            // 2. Specify Kind as UTC directly. DO NOT use ToUniversalTime() which shifts the hour.
            rate.Date = DateTime.SpecifyKind(rate.Date.Date, DateTimeKind.Utc);

            // Ensure Base Currency matches Company
            rate.ExchangeCurrency = baseCurrency.Trim().ToUpperInvariant();

            // Check for Duplicate Rate on same day
            bool exists = await context.CurrencyManagements.AnyAsync(r =>
                r.CompanyId == companyId &&
                r.CurrencyId == rate.CurrencyId &&
                r.Date == rate.Date &&
                r.Id != rate.Id);

            if (exists) return $"A rate for this currency on {rate.Date:yyyy-MM-dd} already exists.";

            if (rate.Id == Guid.Empty)
            {
                rate.Id = Guid.NewGuid();
                rate.CompanyId = companyId;
                context.CurrencyManagements.Add(rate); // This line had a syntax error in your snippet (missing context)
            }
            else
            {
                var existing = await context.CurrencyManagements.FindAsync(rate.Id);
                if (existing == null) return "Rate not found.";

                existing.CurrencyId = rate.CurrencyId;
                existing.Rate = rate.Rate;
                existing.Date = rate.Date;
                existing.ExchangeCurrency = rate.ExchangeCurrency;
            }

            await context.SaveChangesAsync();
            return string.Empty;
        }

        public async Task DeleteRateAsync(Guid companyId, Guid id)
        {
            using var context = _dbFactory.CreateDbContext();
            var item = await context.CurrencyManagements.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);
            if (item != null)
            {
                context.CurrencyManagements.Remove(item);
                await context.SaveChangesAsync();
            }
        }
        public async Task<decimal> GetLatestExchangeRateAsync(Guid companyId, Guid currencyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Find the most recent rate for this currency
            var managementEntry = await ctx.CurrencyManagements // Assuming DbSet is named CurrencyManagements
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.CurrencyId == currencyId)
                .OrderByDescending(x => x.Date) // Get the latest one
                .FirstOrDefaultAsync();

            return managementEntry?.Rate ?? 1.0m; // Default to 1.0 if no rate is defined
        }
    }
}