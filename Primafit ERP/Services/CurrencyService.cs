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

        public async Task<List<Currency>> GetCurrenciesAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.Currencies.OrderBy(c => c.CurrencyName).ToListAsync();
        }

        public async Task<bool> SaveCurrencyAsync(Currency currency)
        {
            using var context = _dbFactory.CreateDbContext();
            if (await context.Currencies.AnyAsync(c => c.CurrencyName == currency.CurrencyName))
                return false;

            if (currency.Id == Guid.Empty) currency.Id = Guid.NewGuid();

            context.Currencies.Add(currency);
            return await context.SaveChangesAsync() > 0;
        }

        
        public async Task<List<CurrencyManagement>> GetRatesAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.CurrencyManagements
                .Include(cm => cm.CurrencyName) 
                .OrderByDescending(cm => cm.Date)
                .ToListAsync();
        }

        public async Task<bool> SaveRateAsync(CurrencyManagement rate)
        {
            using var context = _dbFactory.CreateDbContext();

            if (rate.Id == Guid.Empty)
            {
                rate.Id = Guid.NewGuid();
                context.CurrencyManagements.Add(rate);
            }
            else
            {
                var existing = await context.CurrencyManagements.FindAsync(rate.Id);
                if (existing == null) return false;

                existing.CurrencyId = rate.CurrencyId;
                existing.ExchangeCurrency = rate.ExchangeCurrency;
                existing.Date = rate.Date;
                existing.Rate = rate.Rate;
            }
            return await context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteRateAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();
            var item = await context.CurrencyManagements.FindAsync(id);
            if (item != null)
            {
                context.CurrencyManagements.Remove(item);
                return await context.SaveChangesAsync() > 0;
            }
            return false;
        }
    }
}