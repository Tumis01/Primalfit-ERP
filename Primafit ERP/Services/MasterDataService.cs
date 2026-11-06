using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class MasterDataService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IDocumentNumberingService _numberingService;

        public MasterDataService(IDbContextFactory<AppDbContext> dbFactory, IDocumentNumberingService numberingService)
        {
            _dbFactory = dbFactory;
            _numberingService = numberingService;
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
            if (customer.CustomerGroupId == Guid.Empty) return "Customer Group is required.";
            if (string.IsNullOrWhiteSpace(customer.Name)) return "Customer Name is required.";
            if (!await ctx.CustomerGroups.AnyAsync(x => x.Id == customer.CustomerGroupId && x.CompanyId == customer.CompanyId))
                return "Select a customer group belonging to this company.";
            if (string.IsNullOrWhiteSpace(customer.Code))
            {
                var group = await ctx.CustomerGroups.AsNoTracking().FirstAsync(x => x.Id == customer.CustomerGroupId);
                customer.Code = group.SequenceDefinitionId.HasValue
                    ? await _numberingService.GenerateAndReserveNextNumberAsync(group.SequenceDefinitionId.Value)
                    : await _numberingService.GenerateAndReserveNextNumberAsync(customer.CompanyId, SequenceTargetType.CustomerCode);
            }
            if (customer.Code.Length > 50) return "Customer Code cannot exceed 50 characters.";
            if (await ctx.Customers.AnyAsync(x => x.CompanyId == customer.CompanyId && x.Code.ToLower() == customer.Code.ToLower() && x.Id != customer.Id))
                return $"Customer Code '{customer.Code}' already exists in this company.";

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
            if (vendor.VendorGroupId == Guid.Empty) return "Vendor Group is required.";
            if (string.IsNullOrWhiteSpace(vendor.Name)) return "Vendor Name is required.";
            if (!await ctx.VendorGroups.AnyAsync(x => x.Id == vendor.VendorGroupId && x.CompanyId == vendor.CompanyId))
                return "Select a vendor group belonging to this company.";
            if (string.IsNullOrWhiteSpace(vendor.Code))
            {
                var group = await ctx.VendorGroups.AsNoTracking().FirstAsync(x => x.Id == vendor.VendorGroupId);
                vendor.Code = group.SequenceDefinitionId.HasValue
                    ? await _numberingService.GenerateAndReserveNextNumberAsync(group.SequenceDefinitionId.Value)
                    : await _numberingService.GenerateAndReserveNextNumberAsync(vendor.CompanyId, SequenceTargetType.VendorCode);
            }
            if (vendor.Code.Length > 50) return "Vendor Code cannot exceed 50 characters.";
            if (await ctx.Vendors.AnyAsync(x => x.CompanyId == vendor.CompanyId && x.Code.ToLower() == vendor.Code.ToLower() && x.Id != vendor.Id))
                return $"Vendor Code '{vendor.Code}' already exists in this company.";

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
            return await ctx.Items
                .Include(i => i.Category)
                .Include(i => i.PrimaryUom)
                .Include(i => i.UomConversions).ThenInclude(x => x.Uom)
                .Where(i => i.CompanyId == companyId)
                .ToListAsync();
        }

        public Task<string> SaveItemAsync(Item item) => SaveItemCoreAsync(item, null);

        public Task<string> SaveItemWithUomConversionAsync(Item item, ItemUomConversionLine conversionLine)
            => SaveItemCoreAsync(item, conversionLine);

        public Task<string> SaveItemWithUomConversionsAsync(Item item, IReadOnlyCollection<ItemUomConversionLine> conversions)
            => SaveItemCoreAsync(item, null, conversions);

        private async Task<string> SaveItemCoreAsync(Item item, ItemUomConversionLine? conversionLine, IReadOnlyCollection<ItemUomConversionLine>? conversions = null)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (item.CompanyId == Guid.Empty) return "Security Error: No Company Context.";
            if (string.IsNullOrWhiteSpace(item.Name)) return "Item Name is required.";
            await using var transaction = await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (await ctx.Items.AnyAsync(x => x.Id == item.Id && x.CompanyId != item.CompanyId)) return "Item not found in this company.";
            if (item.CategoryId.HasValue
                && !await ctx.ItemCategories.AnyAsync(x => x.Id == item.CategoryId.Value && x.CompanyId == item.CompanyId))
                return "Select an item category belonging to this company.";

            if (!item.IsService && item.InventoryAssetAccountId == Guid.Empty)
                return "Inventory Asset Account is required for physical goods.";

            var existingItemForCode = item.Id == Guid.Empty ? null : await ctx.Items.Include(x => x.UomConversions).AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == item.Id && x.CompanyId == item.CompanyId);
            if (conversionLine is not null && item.Id != Guid.Empty && existingItemForCode is null)
                return "The item could not be found for this company. Save the item before saving its UOM conversions.";
            var category = item.CategoryId.HasValue
                ? await ctx.ItemCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.CategoryId.Value && x.CompanyId == item.CompanyId)
                : null;
            if (item.CategoryId.HasValue && category is null) return "Select an item category belonging to this company.";
            var sequenceId = category?.ItemCodeSequenceDefinitionId;
            if (sequenceId.HasValue && !await ctx.SequenceDefinitions.AnyAsync(x =>
                x.Id == sequenceId.Value && x.CompanyId == item.CompanyId && x.TargetType == SequenceTargetType.ItemCode))
                return "The selected item category has an invalid item code sequence.";
            var shouldGenerateCode = sequenceId.HasValue && (existingItemForCode is null
                || existingItemForCode.CategoryId != item.CategoryId
                || string.IsNullOrWhiteSpace(item.Code));
            if (shouldGenerateCode && sequenceId is Guid assignedSequenceId)
            {
                try
                {
                    item.Code = await _numberingService.ReserveNextNumberWithinTransactionAsync(
                        ctx, assignedSequenceId, exceptEntityId: item.Id);
                }
                catch (InvalidOperationException ex)
                {
                    return $"Could not generate an item code: {ex.Message}";
                }
            }
            item.Code = item.Code?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(item.Code))
                return "Enter an item code. A code is generated automatically only when the selected category has an assigned sequence.";

            if (!item.IsService)
            {
                UnitOfMeasure? primaryUom = null;
                if (item.UomId.HasValue)
                {
                    primaryUom = await ctx.UnitOfMeasures
                        .FirstOrDefaultAsync(u => u.Id == item.UomId.Value && u.CompanyId == item.CompanyId);
                    if (primaryUom == null) return "The selected primary UOM is invalid.";
                    item.UoM = primaryUom.Name;
                }
                else
                {
                    primaryUom = await ctx.UnitOfMeasures.FirstOrDefaultAsync(x => x.CompanyId == item.CompanyId && x.Name == item.UoM);
                    if (primaryUom == null) return "Select a primary UOM for this inventory item.";
                    item.UomId = primaryUom.Id;
                    item.UoM = primaryUom.Name;
                }

            }
            else
            {
                item.UomId = null;
                item.UoM = string.Empty;
            }

            var primaryBasisChanged = existingItemForCode != null && existingItemForCode.UomId != item.UomId
                && !(existingItemForCode.UomId == null && string.Equals(existingItemForCode.UoM, item.UoM, StringComparison.OrdinalIgnoreCase));
            if (existingItemForCode != null && (primaryBasisChanged || existingItemForCode.IsService != item.IsService)
                && await HasItemActivityAsync(ctx, item.Id))
                return "The primary UOM and inventory/service type cannot change after this item has document or stock activity. Add an equivalent UOM instead.";

            if (conversions != null)
            {
                if (conversions.Count > 0 && (item.IsService || !item.UomId.HasValue)) return "Select a primary UOM for this inventory item before adding equivalent UOMs.";
                if (conversions.Select(x => x.UomId).Distinct().Count() != conversions.Count) return "Each equivalent UOM can only appear once for this item.";
                if (conversions.Any(x => x.UomId == Guid.Empty || x.UomId == item.UomId || x.ConversionFactorToBase <= 0
                    || x.ConversionFactorToBase <= 0.000000000001m || x.ConversionFactorToBase >= 1000000000000m || Math.Round(x.ConversionFactorToBase, 16) != x.ConversionFactorToBase))
                    return "Each equivalent UOM needs a positive conversion ratio and must differ from the primary UOM.";
                var ids = conversions.Select(x => x.UomId).ToList();
                if (await ctx.UnitOfMeasures.CountAsync(x => x.CompanyId == item.CompanyId && ids.Contains(x.Id)) != ids.Count)
                    return "Every equivalent UOM must belong to this company.";
            }

            // Save only the item and the explicitly validated conversion rows.
            item.UomConversions = new();

            UnitOfMeasure? conversionUom = null;
            if (conversionLine is not null)
            {
                if (item.IsService)
                    return "Additional UOM conversions can only be added to physical inventory items.";
                if (!item.UomId.HasValue)
                    return "Select a primary UOM before adding conversion UOMs.";
                if (conversionLine.UomId == Guid.Empty || conversionLine.ConversionFactorToBase <= 0)
                    return "Select an additional UOM and enter a conversion factor greater than zero.";

                conversionUom = await ctx.UnitOfMeasures.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == conversionLine.UomId && u.CompanyId == item.CompanyId);
                if (conversionUom is null)
                    return "The selected UOM could not be found for this company. Refresh the UOM list and try again.";
                if (item.UomId == conversionLine.UomId)
                    return "The primary UOM is already configured on the item.";

                var primaryUomChanged = existingItemForCode is not null && existingItemForCode.UomId != item.UomId;
                if (!primaryUomChanged && existingItemForCode is not null && await ctx.ItemUomConversionLines.AnyAsync(x =>
                    x.ItemId == item.Id && x.UomId == conversionLine.UomId && x.Id != conversionLine.Id))
                    return "This UOM is already configured for the item.";
            }

            if (item.SalesIncomeAccountId == Guid.Empty) return "Sales Income Account is required.";
            if (item.CostOfGoodsSoldAccountId == Guid.Empty) return "COGS/Expense Account is required.";

            bool isCodeDuplicate = await ctx.Items.AnyAsync(i => i.CompanyId == item.CompanyId
                && i.Code.ToLower() == item.Code.ToLower() && i.Id != item.Id);
            if (isCodeDuplicate) return $"Item Code '{item.Code}' is already in use.";

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
                    // Item conversion factors are defined relative to the
                    // primary UOM. Once the primary UOM changes, the previous
                    // conversion rows are no longer valid for this item.
                    // Remove them before saving the new item setup rows so
                    // reports cannot display historical conversions.
                    if (primaryBasisChanged)
                    {
                        var obsoleteConversions = await ctx.ItemUomConversionLines
                            .Where(x => x.ItemId == existingItem.Id)
                            .ToListAsync();
                        ctx.ItemUomConversionLines.RemoveRange(obsoleteConversions);
                    }

                    item.WeightedAverageCost = existingItem.WeightedAverageCost;
                    item.MostRecentCost = existingItem.MostRecentCost;

                    ctx.Entry(existingItem).CurrentValues.SetValues(item);
                }
            }

            if (conversionLine is not null)
            {
                var existingConversion = await ctx.ItemUomConversionLines
                    .FirstOrDefaultAsync(x => x.Id == conversionLine.Id && x.ItemId == item.Id);
                if (existingConversion is null)
                {
                    ctx.ItemUomConversionLines.Add(new ItemUomConversionLine
                    {
                        Id = conversionLine.Id == Guid.Empty ? Guid.NewGuid() : conversionLine.Id,
                        ItemId = item.Id,
                        UomId = conversionLine.UomId,
                        ConversionFactorToBase = conversionLine.ConversionFactorToBase,
                        IsActive = true
                    });
                }
                else
                {
                    existingConversion.UomId = conversionLine.UomId;
                    existingConversion.ConversionFactorToBase = conversionLine.ConversionFactorToBase;
                    existingConversion.IsActive = true;
                }
            }

            if (conversions != null)
            {
                var existing = await ctx.ItemUomConversionLines.Where(x => x.ItemId == item.Id).ToListAsync();
                var selected = conversions.Select(x => x.UomId).ToHashSet();
                ctx.ItemUomConversionLines.RemoveRange(existing.Where(x => !selected.Contains(x.UomId)));
                foreach (var input in conversions)
                {
                    var saved = existing.FirstOrDefault(x => x.UomId == input.UomId && ctx.Entry(x).State != EntityState.Deleted);
                    if (saved == null) { saved = new ItemUomConversionLine { ItemId = item.Id, UomId = input.UomId }; ctx.ItemUomConversionLines.Add(saved); }
                    saved.ConversionFactorToBase = input.ConversionFactorToBase;
                    saved.IsActive = input.IsActive;
                }
            }

            await ctx.SaveChangesAsync();
            await transaction.CommitAsync();
            return string.Empty;
        }

        private static async Task<bool> HasItemActivityAsync(AppDbContext ctx, Guid itemId) =>
            await ctx.StockLedgers.AnyAsync(x => x.ItemId == itemId)
            || await ctx.SalesOrderLines.AnyAsync(x => x.ItemId == itemId)
            || await ctx.PurchaseOrderLines.AnyAsync(x => x.ItemId == itemId)
            || await ctx.SalesQuoteLines.AnyAsync(x => x.ItemId == itemId)
            || await ctx.PurchaseRequestLines.AnyAsync(x => x.ItemId == itemId)
            || await ctx.SalesInvoiceLines.AnyAsync(x => x.ItemId == itemId)
            || await ctx.PurchaseInvoiceLines.AnyAsync(x => x.ItemId == itemId);
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
            var groups = await ctx.CustomerGroups
                .AsNoTracking().Include(x => x.SequenceDefinition)
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Name)
                .ToListAsync();
            if (groups.Count == 0)
            {
                var fallback = new CustomerGroup { CompanyId = companyId, Name = "Default Customer Group" };
                ctx.CustomerGroups.Add(fallback);
                await ctx.SaveChangesAsync();
                groups.Add(fallback);
            }
            return groups;
        }

        public async Task<string> SaveCustomerGroupAsync(CustomerGroup group)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(group.Name)) return "Group Name is required.";
            if (group.SequenceDefinitionId.HasValue && !await ctx.SequenceDefinitions.AnyAsync(x => x.Id == group.SequenceDefinitionId.Value && x.CompanyId == group.CompanyId && x.TargetType == SequenceTargetType.CustomerCode))
                return "Select a customer code sequence belonging to this company.";

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
            var groups = await ctx.VendorGroups
                .AsNoTracking().Include(x => x.SequenceDefinition)
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Name)
                .ToListAsync();
            if (groups.Count == 0)
            {
                var fallback = new VendorGroup { CompanyId = companyId, Name = "Default Vendor Group" };
                ctx.VendorGroups.Add(fallback);
                await ctx.SaveChangesAsync();
                groups.Add(fallback);
            }
            return groups;
        }

        public async Task<string> SaveVendorGroupAsync(VendorGroup group)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(group.Name)) return "Group Name is required.";
            if (group.SequenceDefinitionId.HasValue && !await ctx.SequenceDefinitions.AnyAsync(x => x.Id == group.SequenceDefinitionId.Value && x.CompanyId == group.CompanyId && x.TargetType == SequenceTargetType.VendorCode))
                return "Select a vendor code sequence belonging to this company.";

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
                .Include(u => u.ConversionUom)
                .Where(u => u.CompanyId == companyId)
                .OrderBy(u => u.Name)
                .ToListAsync();
        }

        public Task<List<UomConversionRule>> GetUomConversionRulesAsync(Guid companyId) => Task.FromResult(new List<UomConversionRule>());

        public Task<string> SaveUomConversionRuleAsync(UomConversionRule rule) => Task.FromResult("Define equivalent UOMs on the item's create or edit form. Global UOM conversions are no longer supported.");

        public async Task<string> DeleteUomConversionRuleAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var rule = await ctx.UomConversionRules.FindAsync(id);
            if (rule == null) return string.Empty;
            ctx.UomConversionRules.Remove(rule);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<List<ItemUomConversionLine>> GetItemUomConversionLinesAsync(Guid itemId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.ItemUomConversionLines.AsNoTracking().Include(x => x.Uom)
                .Where(x => x.ItemId == itemId && x.IsActive).OrderBy(x => x.Uom!.Name).ToListAsync();
        }

        public async Task<List<ItemUomConversionLine>> GetItemUomConversionLinesAsync(Guid companyId, bool byCompany)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.ItemUomConversionLines.AsNoTracking().Include(x => x.Uom)
                .Where(x => x.IsActive && x.Item!.CompanyId == companyId).OrderBy(x => x.Uom!.Name).ToListAsync();
        }

        public async Task<string> SaveItemUomConversionLineAsync(ItemUomConversionLine line, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (line.ItemId == Guid.Empty || line.UomId == Guid.Empty || line.ConversionFactorToBase <= 0)
                return "Select a UOM and enter a conversion factor greater than zero.";
            var item = await ctx.Items.Include(x => x.UomConversions).AsNoTracking().FirstOrDefaultAsync(x => x.Id == line.ItemId && x.CompanyId == companyId);
            var uom = await ctx.UnitOfMeasures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == line.UomId && x.CompanyId == companyId);
            if (item == null) return "The item could not be found for this company. Save the item before saving its UOM conversions.";
            if (item.IsService || !item.UomId.HasValue) return "Select a primary UOM on a physical inventory item before defining equivalents.";
            if (line.ConversionFactorToBase <= 0.000000000001m || line.ConversionFactorToBase >= 1000000000000m || Math.Round(line.ConversionFactorToBase, 16) != line.ConversionFactorToBase)
                return "Enter a conversion with up to sixteen decimal places; neither unit may contain a trillion or more of the other unit.";
            if (uom == null) return "The selected UOM could not be found for this company. Refresh the UOM list and try again.";
            if (item.UomId == line.UomId)
                return "The primary UOM is already configured on the item.";
            if (await ctx.ItemUomConversionLines.AnyAsync(x => x.ItemId == line.ItemId && x.UomId == line.UomId && x.Id != line.Id))
                return "This UOM is already configured for the item.";
            // Never pass the detached Uom/Item navigation graph to Add/Update.
            // Item setup loads Uom for display, but this service owns only the
            // conversion line's scalar values and foreign keys.
            var existingLine = line.Id == Guid.Empty
                ? null
                : await ctx.ItemUomConversionLines.FirstOrDefaultAsync(x => x.Id == line.Id && x.ItemId == line.ItemId);
            if (existingLine == null)
            {
                ctx.ItemUomConversionLines.Add(new ItemUomConversionLine
                {
                    Id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id,
                    ItemId = line.ItemId,
                    UomId = line.UomId,
                    ConversionFactorToBase = line.ConversionFactorToBase,
                    IsActive = line.IsActive
                });
            }
            else
            {
                existingLine.UomId = line.UomId;
                existingLine.ConversionFactorToBase = line.ConversionFactorToBase;
                existingLine.IsActive = line.IsActive;
            }
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteItemUomConversionLineAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var line = await ctx.ItemUomConversionLines.FindAsync(id);
            if (line != null) { ctx.ItemUomConversionLines.Remove(line); await ctx.SaveChangesAsync(); }
            return string.Empty;
        }

        public async Task<string> SaveUnitOfMeasureAsync(UnitOfMeasure uom)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (uom.CompanyId == Guid.Empty) return "Security Error: No Company Context.";
            if (string.IsNullOrWhiteSpace(uom.Name)) return "UoM Name is required.";
            if (string.IsNullOrWhiteSpace(uom.Code)) return "UOM code is required. Assign a code before saving this unit.";
            if (uom.Code.Trim().Length > 32) return "UOM code cannot exceed 32 characters.";

            uom.Name = uom.Name.Trim();
            uom.Code = uom.Code.Trim().ToUpperInvariant();
            if (await ctx.UnitOfMeasures.AnyAsync(u => u.CompanyId == uom.CompanyId && u.Id != uom.Id && u.Code != null && u.Code.ToUpper() == uom.Code))
                return $"UOM code '{uom.Code}' already exists in this company.";

            // Duplicate Check
            bool isDuplicate = await ctx.UnitOfMeasures.AnyAsync(u => u.CompanyId == uom.CompanyId && u.Name.ToLower() == uom.Name.ToLower() && u.Id != uom.Id);
            if (isDuplicate) return $"The UoM '{uom.Name}' already exists.";

            var existingUom = uom.Id == Guid.Empty
                ? null
                : await ctx.UnitOfMeasures.FirstOrDefaultAsync(x => x.Id == uom.Id && x.CompanyId == uom.CompanyId);
            if (existingUom == null)
            {
                ctx.UnitOfMeasures.Add(new UnitOfMeasure
                {
                    Id = uom.Id == Guid.Empty ? Guid.NewGuid() : uom.Id,
                    CompanyId = uom.CompanyId,
                    Name = uom.Name,
                    Code = uom.Code,
                    ConversionFactor = string.Empty,
                    ConversionFactorValue = 1m
                });
            }
            else
            {
                existingUom.Name = uom.Name;
                existingUom.Code = uom.Code;
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteUnitOfMeasureAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (await ctx.Items.AnyAsync(i => i.UomId == id || i.AlternateUomId == id))
                return "Cannot delete: this UOM is assigned to one or more inventory items.";
            if (await ctx.ItemUomConversionLines.AnyAsync(x => x.UomId == id))
                return "Cannot delete: this UOM is used by one or more item conversion lines.";
            if (await ctx.UomConversionRules.AnyAsync(x => x.FromUomId == id || x.ToUomId == id))
                return "Cannot delete: this UOM is used by one or more conversion rules.";
            if (await ctx.UnitOfMeasures.AnyAsync(u => u.ConversionUomId == id))
                return "Cannot delete: this UOM is used as a conversion target.";
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
                .Include(c => c.ItemCodeSequenceDefinition)
                .Where(c => c.CompanyId == companyId)
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync();
        }
        public async Task<string> SaveItemCategoryAsync(ItemCategory category)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(category.Name)) return "Category Name is required.";
            if (category.ItemCodeSequenceDefinitionId.HasValue && !await ctx.SequenceDefinitions.AnyAsync(x =>
                x.Id == category.ItemCodeSequenceDefinitionId.Value && x.CompanyId == category.CompanyId && x.TargetType == SequenceTargetType.ItemCode))
                return "Select an item code sequence belonging to this company.";
            if (string.IsNullOrWhiteSpace(category.Code))
                return "Category Code is required.";
            category.Code = category.Code.Trim();
            if (category.Code.Length > 50) return "Category Code cannot exceed 50 characters.";
            if (await ctx.ItemCategories.AnyAsync(x => x.CompanyId == category.CompanyId && x.Code.ToLower() == category.Code.ToLower() && x.Id != category.Id))
                return $"Category Code '{category.Code}' already exists in this company.";

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
        public async Task<List<Warehouse>> GetREAlWarehousesAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Warehouses
                .AsNoTracking()
                .Where(w => w.CompanyId == companyId)
                .OrderBy(w => w.Name)
                .ToListAsync();
        }

    }
}
