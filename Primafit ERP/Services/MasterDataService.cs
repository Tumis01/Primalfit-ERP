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

        // --- CUSTOMERS ---
        public async Task<List<Customer>> GetCustomersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Customers
                .Include(c => c.DefaultCurrency)
                .Include(c => c.Group) // Include Group Name for the grid
                .Where(c => c.CompanyId == companyId)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<string> SaveCustomerAsync(Customer customer)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (customer.CurrencyId == Guid.Empty) return "Default Currency is required.";

            // Note: The auto-fill logic for ReceivablesAccountId happens in the UI (Razor) 
            // via the @onchange event, so we just save whatever the model contains here.

            if (customer.Id == Guid.Empty || !await ctx.Customers.AnyAsync(x => x.Id == customer.Id))
            {
                ctx.Customers.Add(customer);
            }
            else
            {
                ctx.Customers.Update(customer);
            }
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteCustomerAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var c = await ctx.Customers.FindAsync(id);
            if (c != null) { ctx.Customers.Remove(c); await ctx.SaveChangesAsync(); }
            return string.Empty;
        }

        // --- VENDORS ---
        public async Task<List<Vendor>> GetVendorsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Vendors
                .Include(v => v.DefaultCurrency)
                .Include(v => v.Group) // Include Group Name for the grid
                .Where(v => v.CompanyId == companyId)
                .OrderBy(v => v.Name)
                .ToListAsync();
        }

        public async Task<string> SaveVendorAsync(Vendor vendor)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (vendor.CurrencyId == Guid.Empty) return "Default Currency is required.";

            if (vendor.Id == Guid.Empty || !await ctx.Vendors.AnyAsync(x => x.Id == vendor.Id))
            {
                ctx.Vendors.Add(vendor);
            }
            else
            {
                ctx.Vendors.Update(vendor);
            }
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // --- ITEMS ---
        public async Task<List<Item>> GetItemsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Items.Include(i => i.Category).Where(i => i.CompanyId == companyId).ToListAsync();
        }

        public async Task<string> SaveItemAsync(Item item)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (item.CompanyId == Guid.Empty) return "Security Error: No Company Context.";
            if (string.IsNullOrWhiteSpace(item.Name)) return "Item Name is required.";
            if (string.IsNullOrWhiteSpace(item.SKU)) return "Code is required.";

            if (!item.IsService && item.InventoryAssetAccountId == Guid.Empty)
                return "Inventory Asset Account is required for physical goods.";

            if (item.SalesIncomeAccountId == Guid.Empty) return "Sales Income Account is required.";
            if (item.CostOfGoodsSoldAccountId == Guid.Empty) return "COGS/Expense Account is required.";

            bool isSkuDuplicate = await ctx.Items.AnyAsync(i => i.CompanyId == item.CompanyId && i.SKU == item.SKU && i.Id != item.Id);
            if (isSkuDuplicate) return ($"The SKU '{item.SKU}' is already in use.");

            bool isNameDuplicate = await ctx.Items.AnyAsync(i => i.CompanyId == item.CompanyId && i.Name == item.Name && i.Id != item.Id);
            if (isNameDuplicate) return ($"The Item Name '{item.Name}' is already in use.");

            if (item.Id == Guid.Empty || !await ctx.Items.AnyAsync(x => x.Id == item.Id))
            {
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                ctx.Items.Add(item);
            }
            else
            {
                // THE FIX: Protect system-calculated costs from being overwritten by the UI!
                var existingItem = await ctx.Items.FindAsync(item.Id);
                if (existingItem != null)
                {
                    item.WeightedAverageCost = existingItem.WeightedAverageCost;
                    item.MostRecentCost = existingItem.MostRecentCost;

                    ctx.Entry(existingItem).CurrentValues.SetValues(item);
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<string> DeleteVendorAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // In a real app, check PurchaseOrders/Bills
            var v = await ctx.Vendors.FindAsync(id);
            if (v != null) { ctx.Vendors.Remove(v); await ctx.SaveChangesAsync(); }
            return string.Empty;
        }

        public async Task<string> DeleteItemAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // In a real app, check InvoiceLines/POLines
            var i = await ctx.Items.FindAsync(id);
            if (i != null) { ctx.Items.Remove(i); await ctx.SaveChangesAsync(); }
            return string.Empty;
        }
        public async Task<List<CustomerGroup>> GetCustomerGroupsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.CustomerGroups
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Name)
                .ToListAsync();
        }

        public async Task<string> SaveCustomerGroupAsync(CustomerGroup group)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(group.Name)) return "Group Name is required.";

            // Duplicate Check
            if (await ctx.CustomerGroups.AnyAsync(x => x.CompanyId == group.CompanyId && x.Name == group.Name && x.Id != group.Id))
                return "Group Name already exists.";

            if (group.Id == Guid.Empty || !await ctx.CustomerGroups.AnyAsync(x => x.Id == group.Id))
            {
                if (group.Id == Guid.Empty) group.Id = Guid.NewGuid();
                ctx.CustomerGroups.Add(group);
            }
            else
            {
                ctx.CustomerGroups.Update(group);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteCustomerGroupAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // Check usage
            if (await ctx.Customers.AnyAsync(c => c.CustomerGroupId == id))
                return "Cannot delete: This group is assigned to one or more customers.";

            var g = await ctx.CustomerGroups.FindAsync(id);
            if (g != null) { ctx.CustomerGroups.Remove(g); await ctx.SaveChangesAsync(); }
            return string.Empty;
        }

        public async Task<List<VendorGroup>> GetVendorGroupsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.VendorGroups
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Name)
                .ToListAsync();
        }

        public async Task<string> SaveVendorGroupAsync(VendorGroup group)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(group.Name)) return "Group Name is required.";

            // Duplicate Check
            if (await ctx.VendorGroups.AnyAsync(x => x.CompanyId == group.CompanyId && x.Name == group.Name && x.Id != group.Id))
                return "Group Name already exists.";

            if (group.Id == Guid.Empty || !await ctx.VendorGroups.AnyAsync(x => x.Id == group.Id))
            {
                if (group.Id == Guid.Empty) group.Id = Guid.NewGuid();
                ctx.VendorGroups.Add(group);
            }
            else
            {
                ctx.VendorGroups.Update(group);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteVendorGroupAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // Check usage
            if (await ctx.Vendors.AnyAsync(v => v.VendorGroupId == id))
                return "Cannot delete: This group is assigned to one or more vendors.";

            var g = await ctx.VendorGroups.FindAsync(id);
            if (g != null) { ctx.VendorGroups.Remove(g); await ctx.SaveChangesAsync(); }
            return string.Empty;
        }
        // --- UNIT OF MEASURE ---
       
        public async Task<List<UnitOfMeasure>> GetUnitOfMeasuresAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.UnitOfMeasures
                .AsNoTracking()
                .Where(u => u.CompanyId == companyId)
                .OrderBy(u => u.Name)
                .ToListAsync();
        }

        public async Task<string> SaveUnitOfMeasureAsync(UnitOfMeasure uom)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (uom.CompanyId == Guid.Empty) return "Security Error: No Company Context.";
            if (string.IsNullOrWhiteSpace(uom.Name)) return "UoM Name is required.";
            if (string.IsNullOrWhiteSpace(uom.ConversionFactor)) return "Conversion Factor is required.";

            // Duplicate Check
            bool isDuplicate = await ctx.UnitOfMeasures.AnyAsync(u => u.CompanyId == uom.CompanyId && u.Name.ToLower() == uom.Name.ToLower() && u.Id != uom.Id);
            if (isDuplicate) return $"The UoM '{uom.Name}' already exists.";

            if (uom.Id == Guid.Empty || !await ctx.UnitOfMeasures.AnyAsync(x => x.Id == uom.Id))
            {
                if (uom.Id == Guid.Empty) uom.Id = Guid.NewGuid();
                ctx.UnitOfMeasures.Add(uom);
            }
            else
            {
                ctx.UnitOfMeasures.Update(uom);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteUnitOfMeasureAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var uom = await ctx.UnitOfMeasures.FindAsync(id);
            if (uom != null)
            {
                // Optional: You could check if any Items are currently using this UoM string here
                ctx.UnitOfMeasures.Remove(uom);
                await ctx.SaveChangesAsync();
            }
            return string.Empty;
        }
        public async Task<List<ItemCategory>> GetItemCategoriesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.ItemCategories
                .Where(c => c.CompanyId == companyId)
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync();
        }
        public async Task<string> SaveItemCategoryAsync(ItemCategory category)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (await ctx.ItemCategories.AnyAsync(c => c.CompanyId == category.CompanyId && c.Name.ToLower() == category.Name.ToLower() && c.Id != category.Id))
                return "A category with this name already exists.";

            if (category.IsService)
            {
                category.InventoryAssetAccountId = null;
                category.AdjustmentExpenseAccountId = null;
            }

            // Check if it's an empty Guid OR if the record simply doesn't exist in the DB yet
            bool exists = category.Id != Guid.Empty && await ctx.ItemCategories.AnyAsync(c => c.Id == category.Id);

            if (!exists)
            {
                if (category.Id == Guid.Empty) category.Id = Guid.NewGuid();
                ctx.ItemCategories.Add(category);
            }
            else
            {
                ctx.ItemCategories.Update(category);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteItemCategoryAsync(Guid id)
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var entity = await ctx.ItemCategories.FindAsync(id);
                if (entity == null) return "Not found.";

                ctx.ItemCategories.Remove(entity);
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch
            {
                return "Cannot delete this category. It is currently assigned to one or more items in your inventory.";
            }
        }
    }
}