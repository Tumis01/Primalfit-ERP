using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class CurrencyService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public CurrencyService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // ==========================================
        // 1. CURRENCIES
        // ==========================================

        public async Task<List<Currency>> GetCurrenciesAsync(Guid companyId)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            return await context.Currencies.AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .OrderBy(c => c.CurrencyName)
                .ToListAsync();
        }

        public async Task<string> SaveCurrencyAsync(Guid companyId, Currency currency)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            if (companyId == Guid.Empty) return "Select a company first.";

            var code = (currency.CurrencyCode ?? "").Trim().ToUpperInvariant();
            var name = (currency.CurrencyName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                return "Currency Code and Name are required.";

            bool exists = await context.Currencies.AnyAsync(c =>
                c.CompanyId == companyId &&
                (c.CurrencyCode == code || c.CurrencyName.ToLower() == name.ToLower()) &&
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
                if (existing == null) return "Currency record not found.";
                existing.CurrencyCode = code;
                existing.CurrencyName = name;
            }

            await context.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteCurrencyAsync(Guid companyId, Guid currencyId)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            bool inUse = await context.CurrencyManagements.AnyAsync(r => r.CurrencyId == currencyId);
            if (inUse) return "Cannot delete: This currency has recorded exchange rate history.";

            var item = await context.Currencies.FirstOrDefaultAsync(c => c.Id == currencyId && c.CompanyId == companyId);
            if (item != null)
            {
                context.Currencies.Remove(item);
                await context.SaveChangesAsync();
                return string.Empty;
            }
            return "Currency not found.";
        }

        // ==========================================
        // 2. EXCHANGE RATES (MULTIPLE DATES PER CURRENCY)
        // ==========================================

        public async Task<List<CurrencyManagement>> GetRatesAsync(Guid companyId)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            return await context.CurrencyManagements.AsNoTracking()
                .Include(r => r.Currency)
                .Where(r => r.CompanyId == companyId)
                .OrderByDescending(r => r.Date)
                .ThenBy(r => r.Currency!.CurrencyCode)
                .ToListAsync();
        }

        public async Task<string> SaveRateAsync(Guid companyId, CurrencyManagement rate, string baseCurrency)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            if (companyId == Guid.Empty) return "Select a company first.";
            if (rate.CurrencyId == Guid.Empty) return "Select a valid Target Currency.";
            if (rate.Rate <= 0) return "Exchange rate must be greater than zero.";
            if (string.IsNullOrWhiteSpace(baseCurrency)) return "Company Base Currency is not set.";

            // Normalize Date to pure Date boundary (midnight UTC)
            DateTime cleanDate = DateTime.SpecifyKind(rate.Date.Date, DateTimeKind.Utc);
            rate.Date = cleanDate;
            rate.ExchangeCurrency = baseCurrency.Trim().ToUpperInvariant();

            // A currency can have multiple rates, but only ONE unique rate per calendar day
            bool duplicateSameDay = await context.CurrencyManagements.AnyAsync(r =>
                r.CompanyId == companyId &&
                r.CurrencyId == rate.CurrencyId &&
                r.Date.Date == cleanDate.Date &&
                r.Id != rate.Id);

            if (duplicateSameDay)
                return $"A rate for this currency on {cleanDate:yyyy-MM-dd} already exists. You can edit the existing rate or select a different date.";

            if (rate.Id == Guid.Empty)
            {
                rate.Id = Guid.NewGuid();
                rate.CompanyId = companyId;
                context.CurrencyManagements.Add(rate);
            }
            else
            {
                var existing = await context.CurrencyManagements.FindAsync(rate.Id);
                if (existing == null) return "Rate record not found.";

                existing.CurrencyId = rate.CurrencyId;
                existing.Rate = rate.Rate;
                existing.Date = cleanDate;
                existing.ExchangeCurrency = rate.ExchangeCurrency;
            }

            await context.SaveChangesAsync();
            return string.Empty;
        }

        public async Task DeleteRateAsync(Guid companyId, Guid id)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var item = await context.CurrencyManagements.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);
            if (item != null)
            {
                context.CurrencyManagements.Remove(item);
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Retrieves the most recent exchange rate for a currency up to current moment.
        /// </summary>
        public async Task<decimal> GetLatestExchangeRateAsync(Guid companyId, Guid currencyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var entry = await ctx.CurrencyManagements
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.CurrencyId == currencyId)
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            return entry?.Rate ?? 1.0m;
        }

        /// <summary>
        /// Retrieves the effective exchange rate for a currency as of a specific document date.
        /// Finds the rate on or immediately preceding the target date.
        /// </summary>
        public async Task<decimal> GetExchangeRateForDateAsync(Guid companyId, Guid currencyId, DateTime effectiveDate)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            DateTime targetDate = effectiveDate.Date;

            var entry = await ctx.CurrencyManagements
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                         && x.CurrencyId == currencyId
                         && x.Date.Date <= targetDate)
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (entry != null) return entry.Rate;

            // Fallback to earliest recorded rate or 1.0
            var fallback = await ctx.CurrencyManagements
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.CurrencyId == currencyId)
                .OrderBy(x => x.Date)
                .FirstOrDefaultAsync();

            return fallback?.Rate ?? 1.0m;
        }
    }
}