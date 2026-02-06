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
                .Where(c => c.CompanyId == companyId)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<string> SaveCustomerAsync(Customer customer)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (customer.CurrencyId == Guid.Empty) return "Default Currency is required.";

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

        // --- VENDORS ---
        public async Task<List<Vendor>> GetVendorsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Vendors
                .Include(v => v.DefaultCurrency)
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
        public async Task<string> SaveItemAsync(Item item)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Security & Basic Validation
            if (item.CompanyId == Guid.Empty) return "Security Error: No Company Context.";
            if (string.IsNullOrWhiteSpace(item.Name)) return "Item Name is required.";
            if (string.IsNullOrWhiteSpace(item.SKU)) return "SKU is required.";

            // 2. Validate GL Accounts (Only if it's a physical good)
            if (!item.IsService)
            {
                if (item.InventoryAssetAccountId == Guid.Empty)
                    return "Inventory Asset Account is required for physical goods.";
            }

            if (item.SalesIncomeAccountId == Guid.Empty) return "Sales Income Account is required.";
            if (item.CostOfGoodsSoldAccountId == Guid.Empty) return "COGS/Expense Account is required.";

            // 3. Check for Duplicate SKU (Prevent collisions)
            // We check if any OTHER item has the same SKU in this company
            bool isDuplicate = await ctx.Items.AnyAsync(i => i.CompanyId == item.CompanyId
                                                          && i.SKU == item.SKU
                                                          && i.Id != item.Id);
            if (isDuplicate)
                return ($"The SKU '{item.SKU}' is already in use by another item.");

            // 4. Save Logic (Insert vs Update)
            if (item.Id == Guid.Empty || !await ctx.Items.AnyAsync(x => x.Id == item.Id))
            {
                // New Item
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                ctx.Items.Add(item);
            }
            else
            {
                // Update Existing
                ctx.Items.Update(item);
            }

            await ctx.SaveChangesAsync();
            return string.Empty; // Success
        }
        // --- ITEMS (Helper) ---
        public async Task<List<Item>> GetItemsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Items.Where(i => i.CompanyId == companyId).ToListAsync();
        }
        

        // --- CUSTOMER DELETE ---
        public async Task<string> DeleteCustomerAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // Check constraints (e.g., invoices)
            bool hasData = await ctx.SalesInvoices.AnyAsync(x => x.CustomerId == id)
                        || await ctx.SalesOrders.AnyAsync(x => x.CustomerId == id);

            if (hasData) return "Cannot delete: This customer has transaction history.";

            var c = await ctx.Customers.FindAsync(id);
            if (c != null)
            {
                ctx.Customers.Remove(c);
                await ctx.SaveChangesAsync();
            }
            return string.Empty;
        }

        // --- VENDOR DELETE ---
        public async Task<string> DeleteVendorAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // Check constraints
            bool hasData = await ctx.VendorBills.AnyAsync(x => x.VendorId == id)
                        || await ctx.PurchaseOrders.AnyAsync(x => x.VendorId == id);

            if (hasData) return "Cannot delete: This vendor has transaction history.";

            var v = await ctx.Vendors.FindAsync(id);
            if (v != null)
            {
                ctx.Vendors.Remove(v);
                await ctx.SaveChangesAsync();
            }
            return string.Empty;
        }

        // --- ITEM DELETE ---
        public async Task<string> DeleteItemAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // Check constraints
            bool hasData = await ctx.SalesInvoiceLines.AnyAsync(x => x.ItemId == id)
                        || await ctx.PurchaseOrderLines.AnyAsync(x => x.ItemId == id);

            if (hasData) return "Cannot delete: This item has been used in transactions.";

            var i = await ctx.Items.FindAsync(id);
            if (i != null)
            {
                ctx.Items.Remove(i);
                await ctx.SaveChangesAsync();
            }
            return string.Empty;
        }
    }
}