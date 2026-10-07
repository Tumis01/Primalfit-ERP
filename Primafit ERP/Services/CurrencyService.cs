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
                if (existing.IsBaseCurrency) return "Edit the base currency in Company Details.";
                existing.CurrencyCode = code;
                existing.CurrencyName = name;
            }

            await context.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteCurrencyAsync(Guid companyId, Guid currencyId)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            var item = await context.Currencies.FirstOrDefaultAsync(c => c.Id == currencyId && c.CompanyId == companyId);
            if (item != null)
            {
                if (item.IsBaseCurrency) return "The base currency cannot be deleted.";
                bool inUse = await context.CurrencyManagements.AnyAsync(r =>
                    r.CurrencyId == currencyId || r.ExchangeCurrencyId == currencyId);
                if (inUse) return "Cannot delete: This currency is used by exchange rate history.";
                if (await context.SegChartOfAccounts.AnyAsync(a => a.CurrencyId == currencyId))
                    return "Cannot delete: This currency is assigned to one or more COA accounts.";

                context.Currencies.Remove(item);
                await context.SaveChangesAsync();
                return string.Empty;
            }
            return "Currency not found.";
        }

        // ==========================================
        // 2. EXCHANGE RATES (MULTIPLE DATES PER CURRENCY PAIR)
        // ==========================================

        public async Task<List<CurrencyManagement>> GetRatesAsync(Guid companyId)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            return await context.CurrencyManagements.AsNoTracking()
                .Include(r => r.Currency)
                .Include(r => r.ExchangeCurrencyRecord)
                .Where(r => r.CompanyId == companyId)
                .OrderByDescending(r => r.Date)
                .ThenBy(r => r.Currency!.CurrencyCode)
                .ToListAsync();
        }

        public async Task<string> SaveRateAsync(Guid companyId, CurrencyManagement rate)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            if (companyId == Guid.Empty) return "Select a company first.";
            if (rate.CurrencyId == Guid.Empty) return "Select a valid source currency.";
            if (!rate.ExchangeCurrencyId.HasValue || rate.ExchangeCurrencyId.Value == Guid.Empty)
                return "Select a valid target currency.";
            if (rate.Rate <= 0) return "Exchange rate must be greater than zero.";

            var source = await context.Currencies.FirstOrDefaultAsync(c => c.Id == rate.CurrencyId && c.CompanyId == companyId);
            var target = await context.Currencies.FirstOrDefaultAsync(c => c.Id == rate.ExchangeCurrencyId && c.CompanyId == companyId);
            if (source == null || target == null) return "Select currencies belonging to this company.";
            if (source.Id == target.Id) return "Source and target currencies must be different.";

            // Normalize Date to pure Date boundary (midnight UTC)
            DateTime cleanDate = DateTime.SpecifyKind(rate.Date.Date, DateTimeKind.Utc);
            rate.Date = cleanDate;
            rate.CompanyId = companyId;
            rate.ExchangeCurrency = target.CurrencyCode;

            // Rates are direct source-to-target quotes. Reverse rates are entered separately.
            bool duplicateSameDay = await context.CurrencyManagements.AnyAsync(r =>
                r.CompanyId == companyId &&
                r.CurrencyId == rate.CurrencyId &&
                r.ExchangeCurrencyId == rate.ExchangeCurrencyId &&
                r.Date.Date == cleanDate.Date &&
                r.Id != rate.Id);

            if (duplicateSameDay)
                return $"A rate for this currency pair on {cleanDate:yyyy-MM-dd} already exists. Edit the existing rate or select a different date.";

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
                if (existing.CompanyId != companyId) return "Rate record not found.";

                existing.CurrencyId = rate.CurrencyId;
                existing.ExchangeCurrencyId = rate.ExchangeCurrencyId;
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
                if (item.CurrencyId == item.ExchangeCurrencyId &&
                    await context.Currencies.AnyAsync(c => c.Id == item.CurrencyId && c.IsBaseCurrency))
                    return;

                context.CurrencyManagements.Remove(item);
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Retrieves the most recent exchange rate from a currency into the company base currency.
        /// </summary>
        public async Task<decimal> GetLatestExchangeRateAsync(Guid companyId, Guid currencyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var (baseCurrencyId, baseCurrencyCode) = await GetBaseCurrencyAsync(ctx, companyId);
            if (baseCurrencyId == currencyId) return 1.0m;

            var entry = await GetBaseRateQuery(ctx, companyId, currencyId, baseCurrencyId, baseCurrencyCode)
                .AsNoTracking()
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            return entry?.Rate ?? 1.0m;
        }

        /// <summary>
        /// Retrieves the effective exchange rate into the company base currency as of a document date.
        /// Finds the rate on or immediately preceding the target date.
        /// </summary>
        public async Task<decimal> GetExchangeRateForDateAsync(Guid companyId, Guid currencyId, DateTime effectiveDate)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            DateTime targetDate = effectiveDate.Date;

            var (baseCurrencyId, baseCurrencyCode) = await GetBaseCurrencyAsync(ctx, companyId);
            if (baseCurrencyId == currencyId) return 1.0m;

            var rates = GetBaseRateQuery(ctx, companyId, currencyId, baseCurrencyId, baseCurrencyCode);
            var entry = await rates
                .AsNoTracking()
                .Where(x => x.Date.Date <= targetDate)
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (entry != null) return entry.Rate;

            // Fallback to earliest recorded rate or 1.0
            var fallback = await rates
                .AsNoTracking()
                .OrderBy(x => x.Date)
                .FirstOrDefaultAsync();

            return fallback?.Rate ?? 1.0m;
        }

        private static async Task<(Guid? Id, string? Code)> GetBaseCurrencyAsync(AppDbContext context, Guid companyId)
        {
            var currency = await context.Currencies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.IsBaseCurrency);
            if (currency != null) return (currency.Id, currency.CurrencyCode);

            var configuredCode = await context.CompanyDetails.AsNoTracking()
                .Where(c => c.CompanyDetailsId == companyId)
                .Select(c => c.FunctionalCurrency)
                .FirstOrDefaultAsync();
            return (null, configuredCode?.Trim().ToUpperInvariant());
        }

        private static IQueryable<CurrencyManagement> GetBaseRateQuery(
            AppDbContext context, Guid companyId, Guid currencyId, Guid? baseCurrencyId, string? baseCurrencyCode)
        {
            var query = context.CurrencyManagements
                .Where(rate => rate.CompanyId == companyId && rate.CurrencyId == currencyId);

            if (baseCurrencyId.HasValue)
            {
                query = query.Where(rate => rate.ExchangeCurrencyId == baseCurrencyId.Value ||
                    (!rate.ExchangeCurrencyId.HasValue && rate.ExchangeCurrency == baseCurrencyCode));
            }
            else if (!string.IsNullOrWhiteSpace(baseCurrencyCode))
            {
                query = query.Where(rate => rate.ExchangeCurrency == baseCurrencyCode);
            }

            return query;
        }
    }
}
