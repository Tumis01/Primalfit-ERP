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

        public async Task<List<Tax>> GetTaxesAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.Taxes.OrderBy(t => t.TaxCode).ToListAsync();
        }

        // Returns "Success" (empty string) or an Error Message
        public async Task<string> SaveTaxAsync(Tax tax)
        {
            using var context = _dbFactory.CreateDbContext();

            // 1. Check for Null/Empty Values
            if (string.IsNullOrWhiteSpace(tax.TaxCode) || string.IsNullOrWhiteSpace(tax.TaxName))
            {
                return "Cannot save null values. Please enter both Tax Code and Tax Name.";
            }

            // 2. Check for Duplicates (Name or Code)
            // We ensure we don't count the *current* record against itself (t.Id != tax.Id)
            bool codeExists = await context.Taxes
                .AnyAsync(t => t.TaxCode == tax.TaxCode && t.Id != tax.Id);

            if (codeExists)
            {
                return $"The Tax Code '{tax.TaxCode}' already exists.";
            }

            bool nameExists = await context.Taxes
                .AnyAsync(t => t.TaxName == tax.TaxName && t.Id != tax.Id);

            if (nameExists)
            {
                return $"The Tax Name '{tax.TaxName}' already exists.";
            }

            // 3. Save Logic
            if (tax.Id == Guid.Empty)
            {
                tax.Id = Guid.NewGuid();
                context.Taxes.Add(tax);
            }
            else
            {
                var existing = await context.Taxes.FindAsync(tax.Id);
                if (existing == null) return "Record not found.";

                existing.TaxCode = tax.TaxCode;
                existing.TaxName = tax.TaxName;
                existing.Per = tax.Per;
            }

            await context.SaveChangesAsync();
            return string.Empty; // Success
        }

        public async Task<bool> DeleteTaxAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();
            var tax = await context.Taxes.FindAsync(id);
            if (tax == null) return false;

            context.Taxes.Remove(tax);
            return await context.SaveChangesAsync() > 0;
        }
    }
}