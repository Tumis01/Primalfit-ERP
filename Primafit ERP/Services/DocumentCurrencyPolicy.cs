using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

internal static class DocumentCurrencyPolicy
{
    public static async Task<string?> ValidateAsync(AppDbContext db, Guid companyId, Guid currencyId, decimal exchangeRate, Guid? expectedCurrencyId = null)
    {
        if (expectedCurrencyId.HasValue && expectedCurrencyId != currencyId)
            return "Document currency is inherited from the customer, supplier or source document and cannot be changed.";
        var currency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == currencyId && x.CompanyId == companyId);
        if (currency == null) return "The document currency is unavailable for this company.";
        if (exchangeRate <= 0 || Math.Round(exchangeRate, 4) != exchangeRate) return "Enter a positive exchange rate with up to four decimal places.";
        if (currency.IsBaseCurrency && exchangeRate != 1m) return "The base currency exchange rate is fixed at 1. Only foreign currency rates can be edited.";
        return null;
    }
}
