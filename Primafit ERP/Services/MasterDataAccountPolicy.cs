using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public static class MasterDataAccountPolicy
{
    public static bool IsEligible(SegChartOfAccount account, IEnumerable<SegAccountType> types, bool balanceSheet, bool debit, bool requireControlAccount = true) =>
        account.IsActive && (!requireControlAccount || account.IsControlAccount) && types.Any(t => t.Id == account.SegAccountTypeId
            && t.IsBalanceSheet == balanceSheet && t.IsDebit == debit);

    public static async Task<string> ValidateAsync(AppDbContext db, Guid companyId,
        params (Guid? AccountId, bool BalanceSheet, bool Debit, string Label, bool RequireControlAccount)[] selections)
    {
        foreach (var selection in selections.Where(s => s.AccountId.HasValue && s.AccountId != Guid.Empty))
        {
            if (!await db.SegChartOfAccounts.AnyAsync(a => a.Id == selection.AccountId && a.CompanyId == companyId
                && a.IsActive && (!selection.RequireControlAccount || a.IsControlAccount) && db.Set<SegAccountType>().Any(t => t.Id == a.SegAccountTypeId
                    && t.IsBalanceSheet == selection.BalanceSheet && t.IsDebit == selection.Debit)))
                return $"{selection.Label}: select an active{(selection.RequireControlAccount ? " control" : "")} account of the required account type belonging to this company.";
        }
        return string.Empty;
    }
}
