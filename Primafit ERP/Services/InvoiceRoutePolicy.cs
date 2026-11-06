using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services;

public static class InvoiceRoutePolicy
{
    public static async Task<string?> ValidateAsync(AppDbContext db, Guid companyId, TransactionTemplateModule module, InvoiceRoute route)
    {
        if (route.TemplateId.HasValue && !await db.CustomTransactionTypes.AnyAsync(x => x.Id == route.TemplateId && x.CompanyId == companyId
            && (x.Module == TransactionTemplateModule.General || x.Module == module)
            && db.TransactionGlMappings.Any(m => m.CompanyId == companyId && m.CustomTransactionTypeId == x.Id && m.IsActive)))
            return "The selected route template is unavailable for this module.";
        var ids = new[] { route.DebitAccountId, route.CreditAccountId, route.TaxAccountId, route.DiscountAccountId }
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (await db.SegChartOfAccounts.CountAsync(x => ids.Contains(x.Id) && x.CompanyId == companyId && x.IsActive) != ids.Count)
            return "Every route account must be active and belong to this company.";
        return null;
    }
}
