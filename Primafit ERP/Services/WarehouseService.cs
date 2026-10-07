using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class WarehouseService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public WarehouseService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // GET: Fetch warehouses for a specific company
        public async Task<List<Warehouse>> GetWarehousesAsync(Guid companyId, WarehouseType? moduleType = null)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var query = ctx.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId);
            if (moduleType.HasValue)
                query = query.Where(w => w.Type == WarehouseType.Both || w.Type == moduleType.Value);
            return await query.OrderBy(w => w.Name).ToListAsync();
        }

        // SAVE: Create or Update (FIXED)
        public async Task<string> SaveWarehouseAsync(Warehouse warehouse)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (warehouse.CompanyId == Guid.Empty) return "Error: Company not selected.";
            if (string.IsNullOrWhiteSpace(warehouse.Name)) return "Error: Warehouse Name is required.";
            if (!Enum.IsDefined(warehouse.Type)) return "Error: Select a valid warehouse type.";

            // Check for duplicate name within the same company (Exclude itself if editing)
            bool duplicateExists = await ctx.Warehouses.AnyAsync(w =>
                w.CompanyId == warehouse.CompanyId &&
                w.Name.ToLower() == warehouse.Name.Trim().ToLower() &&
                w.Id != warehouse.Id);

            if (duplicateExists) return $"Error: A warehouse named '{warehouse.Name}' already exists.";

            // 1. Try to find the record in the DB
            var existing = await ctx.Warehouses.FindAsync(warehouse.Id);

            if (existing == null)
            {
                // CASE: NEW WAREHOUSE
                // Even if the model has an ID (Guid.NewGuid()), if it's not in DB, it's new.
                if (warehouse.Id == Guid.Empty) warehouse.Id = Guid.NewGuid();

                ctx.Warehouses.Add(warehouse);
            }
            else
            {
                // CASE: UPDATE EXISTING
                existing.Name = warehouse.Name;
                existing.Location = warehouse.Location;
                existing.Type = warehouse.Type;
                // We do NOT update CompanyId generally, but you can if needed
            }

            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                return $"Database Error: {ex.Message}";
            }
        }

        // DELETE
        public async Task<string> DeleteWarehouseAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var warehouse = await ctx.Warehouses.FindAsync(id);

            if (warehouse == null) return "Error: Warehouse not found.";

            // Check dependencies before delete (Optional but recommended)
            bool hasStock = await ctx.StockLedgers.AnyAsync(s => s.WarehouseId == id);
            if (hasStock) return "Error: Cannot delete warehouse because it has stock history.";

            ctx.Warehouses.Remove(warehouse);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}
