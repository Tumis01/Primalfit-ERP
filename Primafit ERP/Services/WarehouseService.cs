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
        public async Task<List<Warehouse>> GetWarehousesAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Warehouses
                .AsNoTracking()
                .Where(w => w.CompanyId == companyId)
                .OrderBy(w => w.Name)
                .ToListAsync();
        }

        // SAVE: Create or Update (Returns string.Empty on success, or error message)
        public async Task<string> SaveWarehouseAsync(Warehouse warehouse)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            if (warehouse.CompanyId == Guid.Empty)
                return "Error: Company not selected.";

            if (string.IsNullOrWhiteSpace(warehouse.Name))
                return "Error: Warehouse Name is required.";

            // Check for duplicate name within the same company
            bool exists = await ctx.Warehouses.AnyAsync(w =>
                w.CompanyId == warehouse.CompanyId &&
                w.Name.ToLower() == warehouse.Name.Trim().ToLower() &&
                w.Id != warehouse.Id);

            if (exists) return "Error: A warehouse with this name already exists.";

            if (warehouse.Id == Guid.Empty)
            {
                // Create New
                warehouse.Id = Guid.NewGuid();
                ctx.Warehouses.Add(warehouse);
            }
            else
            {
                // Update Existing
                var existing = await ctx.Warehouses.FindAsync(warehouse.Id);
                if (existing == null) return "Error: Warehouse not found.";

                existing.Name = warehouse.Name;
                existing.Location = warehouse.Location;
            }

            await ctx.SaveChangesAsync();
            return string.Empty; // Success
        }

        // DELETE
        public async Task<string> DeleteWarehouseAsync(Guid id)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var warehouse = await ctx.Warehouses.FindAsync(id);

            if (warehouse == null) return "Error: Warehouse not found.";

           

            ctx.Warehouses.Remove(warehouse);
            await ctx.SaveChangesAsync();
            return string.Empty; // Success
        }
    }
}