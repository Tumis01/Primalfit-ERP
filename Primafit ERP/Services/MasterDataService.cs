using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class MasterDataService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public MasterDataService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- BUSINESS PARTNERS ---
        public async Task<List<BusinessPartner>> GetPartnersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BusinessPartners.AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<string> SavePartnerAsync(BusinessPartner partner)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(partner.Name)) return "Name is required.";
            if (partner.CompanyId == Guid.Empty) return "Company is required.";

            // 1. Try to find the record in the database
            var existing = await ctx.BusinessPartners.FindAsync(partner.Id);

            if (existing == null)
            {
                // CASE: NEW RECORD
                // Even if 'partner.Id' is not empty (due to model auto-init), 
                // if it's not in the DB, we treat it as new.

                if (partner.Id == Guid.Empty) partner.Id = Guid.NewGuid(); // Ensure valid ID

                // Important: Ensure we are not tracking a duplicate instance if passed from UI
                ctx.BusinessPartners.Add(partner);
            }
            else
            {
                // CASE: UPDATE EXISTING
                // Map fields from the UI object (partner) to the DB object (existing)
                existing.Name = partner.Name;
                existing.Email = partner.Email;
                existing.Phone = partner.Phone;
                existing.TaxId = partner.TaxId;
                existing.IsCustomer = partner.IsCustomer;
                existing.IsVendor = partner.IsVendor;
                existing.ReceivablesAccountId = partner.ReceivablesAccountId;
                existing.PayablesAccountId = partner.PayablesAccountId;

                // No need to call Update(); EF Core tracks 'existing' automatically
            }

            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Database Error: {ex.Message}";
            }
        }

        // --- ITEMS ---
        public async Task<List<Item>> GetItemsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Items.AsNoTracking()
                .Where(i => i.CompanyId == companyId)
                .OrderBy(i => i.Name)
                .ToListAsync();
        }

        public async Task<string> SaveItemAsync(Item item)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.SKU))
                return "Name and SKU are required.";

            // Validate GL Links (Optional: You might want to allow saving draft items without GL accounts initially)
            if (item.InventoryAssetAccountId == Guid.Empty) return "Inventory Asset Account is required.";
            if (item.CostOfGoodsSoldAccountId == Guid.Empty) return "COGS Account is required.";
            if (item.SalesIncomeAccountId == Guid.Empty) return "Sales Income Account is required.";

            // 1. Try to find the record
            var existing = await ctx.Items.FindAsync(item.Id);

            if (existing == null)
            {
                // CASE: NEW ITEM
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                ctx.Items.Add(item);
            }
            else
            {
                // CASE: UPDATE ITEM
                existing.SKU = item.SKU;
                existing.Name = item.Name;
                existing.UoM = item.UoM;
                existing.ReorderLevel = item.ReorderLevel;
                existing.SellingPrice = item.SellingPrice;

                // Note: We typically protect WACC from manual edits unless necessary
                // existing.WeightedAverageCost = item.WeightedAverageCost; 

                existing.InventoryAssetAccountId = item.InventoryAssetAccountId;
                existing.CostOfGoodsSoldAccountId = item.CostOfGoodsSoldAccountId;
                existing.SalesIncomeAccountId = item.SalesIncomeAccountId;
                existing.AdjustmentExpenseAccountId = item.AdjustmentExpenseAccountId;
            }

            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Database Error: {ex.Message}";
            }
        }
    }
}