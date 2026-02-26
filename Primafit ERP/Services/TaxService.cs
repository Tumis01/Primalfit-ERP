using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class TaxService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public TaxService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<Tax>> GetTaxesAsync(Guid companyId)
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.Taxes
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId)
                .OrderBy(t => t.TaxCode)
                .ToListAsync();
        }

        public async Task<string> SaveTaxAsync(Guid companyId, Tax tax)
        {
            using var context = _dbFactory.CreateDbContext();

            if (companyId == Guid.Empty) return "Select a company first.";

            tax.TaxCode = (tax.TaxCode ?? "").Trim().ToUpperInvariant();
            tax.TaxName = (tax.TaxName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(tax.TaxCode) || string.IsNullOrWhiteSpace(tax.TaxName))
                return "Tax Code and Tax Name are required.";

            if (tax.Per < 0 || tax.Per > 100)
                return "Tax percentage must be between 0 and 100.";

            // Check Duplicates
            bool codeExists = await context.Taxes.AnyAsync(t => t.CompanyId == companyId && t.TaxCode == tax.TaxCode && t.Id != tax.Id);
            if (codeExists) return $"The Tax Code '{tax.TaxCode}' already exists.";

            bool nameExists = await context.Taxes.AnyAsync(t => t.CompanyId == companyId && t.TaxName == tax.TaxName && t.Id != tax.Id);
            if (nameExists) return $"The Tax Name '{tax.TaxName}' already exists.";

            if (tax.Id == Guid.Empty)
            {
                tax.Id = Guid.NewGuid();
                tax.CompanyId = companyId;
                context.Taxes.Add(tax);
            }
            else
            {
                var existing = await context.Taxes.FirstOrDefaultAsync(t => t.Id == tax.Id && t.CompanyId == companyId);
                if (existing == null) return "Record not found.";

                existing.TaxCode = tax.TaxCode;
                existing.TaxName = tax.TaxName;
                existing.Per = tax.Per;
                // IMPORTANT: Linking to Segmented COA
                existing.GLAccountId = tax.GLAccountId;
            }

            await context.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<bool> DeleteTaxAsync(Guid companyId, Guid id)
        {
            using var context = _dbFactory.CreateDbContext();
            var tax = await context.Taxes.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);
            if (tax == null) return false;
            context.Taxes.Remove(tax);
            return await context.SaveChangesAsync() > 0;
        }
    }
}