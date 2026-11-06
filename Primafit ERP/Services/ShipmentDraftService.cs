using System.Data;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed record ShipmentOrderCandidate(Guid OrderId, string OrderNumber, Guid LineId, Guid ItemId, string ItemName,
    Guid? UomId, string UomName, decimal UomConversionFactor, decimal Ordered, decimal Shipped, decimal Reserved, decimal Remaining, Guid? DimensionSetId = null);
public sealed record ShipmentDraftLineInput(Guid OrderLineId, decimal QuantityBase, Guid? UomId, string UomName, decimal UomConversionFactor, Guid? DimensionSetId = null, bool OverrideDimensions = false);

public sealed class ShipmentDraftService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IDocumentNumberingService _numbering;
    public ShipmentDraftService(IDbContextFactory<AppDbContext> factory, IDocumentNumberingService numbering) { _factory = factory; _numbering = numbering; }

    public async Task<List<ShipmentOrderCandidate>> GetOrderCandidatesAsync(Guid companyId, Guid customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await GetCandidatesAsync(db, companyId, customerId);
    }

    public async Task<List<ShipmentOrderCandidate>> GetInvoiceCandidatesAsync(Guid companyId, Guid customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await GetInvoiceCandidatesAsync(db, companyId, customerId, null);
    }

    // Quantities are stored in base units. For an invoice linked to an order,
    // attribute order dispatches to it after removing dispatches already linked
    // to other invoice lines. This also accounts for later order-sourced dispatches.
    internal static async Task<List<ShipmentOrderCandidate>> GetInvoiceCandidatesAsync(
        AppDbContext db, Guid companyId, Guid customerId, Guid? excludedShipmentId)
    {
        var lines = await db.SalesInvoiceLines.AsNoTracking()
            .Include(x => x.SalesInvoice).Include(x => x.Item).Include(x => x.SalesOrderLine).ThenInclude(x => x!.Header)
            .Include(x => x.ShipmentLine).ThenInclude(x => x!.Shipment)
            .Where(x => x.SalesInvoice != null && x.SalesInvoice.CompanyId == companyId
                && x.SalesInvoice.CustomerId == customerId && !x.SalesInvoice.IsDirectInvoice
                && (x.SalesInvoice.Status == SalesInvoiceStatus.Posted || x.SalesInvoice.Status == SalesInvoiceStatus.InReview) && x.SalesInvoice.GLBatchId.HasValue
                && db.GLBatches.Any(b => b.Id == x.SalesInvoice.GLBatchId.Value && b.CompanyId == companyId && b.Status == BatchStatus.Posted)
                && x.ItemId.HasValue && x.Item != null && x.Item.CompanyId == companyId && !x.Item.IsService && x.QuantityInvoiced > 0)
            .ToListAsync();
        var rootIds = lines.Where(x => x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).Distinct().ToList();
        var lineIds = lines.Select(x => x.Id).ToList();
        var sourceBatchIds = lines.Where(x => x.ShipmentLine?.Shipment?.ShipmentBatchId != null)
            .Select(x => x.ShipmentLine!.Shipment!.ShipmentBatchId!.Value).Distinct().ToList();
        var postedSourceBatches = await db.GLBatches.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == BatchStatus.Posted && sourceBatchIds.Contains(x.Id)).Select(x => x.Id).ToHashSetAsync();
        var movements = await db.SalesShipmentLines.AsNoTracking()
            .Where(x => x.Shipment!.CompanyId == companyId && x.ShipmentId != excludedShipmentId
                && (x.Shipment.Status == ShipmentStatus.Shipped || x.Shipment.Status == ShipmentStatus.Pending)
                && ((x.SalesOrderLineId.HasValue && rootIds.Contains(x.SalesOrderLineId.Value))
                    || (x.SalesInvoiceLineId.HasValue && lineIds.Contains(x.SalesInvoiceLineId.Value))))
            .Select(x => new { x.Id, x.SalesOrderLineId, x.SalesInvoiceLineId, x.QtyShipped, x.Shipment!.Status }).ToListAsync();
        var allocations = await db.SalesInvoiceLines.AsNoTracking()
            .Where(x => x.SalesOrderLineId.HasValue && rootIds.Contains(x.SalesOrderLineId.Value) && x.ShipmentLineId.HasValue)
            .Select(x => new { x.Id, x.SalesOrderLineId, x.ShipmentLineId }).ToListAsync();
        var credits = await InvoiceCorrectionBalances.SalesOrderAdjustmentsAsync(db, rootIds);
        var invoiceCredits = await InvoiceCorrectionBalances.SalesInvoiceAdjustmentsAsync(db, lineIds);
        var result = new List<ShipmentOrderCandidate>();
        foreach (var line in lines)
        {
            decimal shipped, reserved, remaining;
            if (line.SalesOrderLineId.HasValue)
            {
                if (line.SalesOrderLine?.Header == null || line.SalesOrderLine.Header.CompanyId != companyId
                    || line.SalesOrderLine.Header.CustomerId != customerId || line.SalesOrderLine.Header.CurrencyId != line.SalesInvoice!.CurrencyId
                    || line.SalesOrderLine.ItemId != line.ItemId || line.ShipmentLine?.Shipment == null
                    || line.ShipmentLine.Shipment.CompanyId != companyId || line.ShipmentLine.Shipment.Status != ShipmentStatus.Shipped
                    || !line.ShipmentLine.Shipment.ShipmentBatchId.HasValue || !postedSourceBatches.Contains(line.ShipmentLine.Shipment.ShipmentBatchId.Value)
                    || line.ShipmentLine.SalesOrderLineId != line.SalesOrderLineId || line.ShipmentLine.ItemId != line.ItemId) continue;
                var root = line.SalesOrderLineId.Value;
                var physical = Math.Max(line.SalesOrderLine.QtyShipped, movements.Where(x => x.SalesOrderLineId == root && x.Status == ShipmentStatus.Shipped).Sum(x => x.QtyShipped));
                var otherAllocatedIds = allocations.Where(x => x.SalesOrderLineId == root && x.Id != line.Id).Select(x => x.ShipmentLineId!.Value).ToHashSet();
                var otherAllocated = movements.Where(x => otherAllocatedIds.Contains(x.Id) && x.Status == ShipmentStatus.Shipped).Sum(x => x.QtyShipped);
                shipped = Math.Max(0, physical - otherAllocated);
                reserved = movements.Where(x => x.SalesOrderLineId == root && x.Status == ShipmentStatus.Pending).Sum(x => x.QtyShipped);
                remaining = Math.Min(Math.Max(0, line.QuantityInvoiced - shipped - reserved - invoiceCredits.GetValueOrDefault(line.Id)),
                    Math.Max(0, line.SalesOrderLine.Quantity - physical - reserved - credits.GetValueOrDefault(root)));
            }
            else
            {
                // A missing order behind a linked shipment is unresolved legacy
                // data, not an unfulfilled manual invoice line.
                if (line.ShipmentLineId.HasValue) continue;
                shipped = movements.Where(x => x.SalesInvoiceLineId == line.Id && x.Status == ShipmentStatus.Shipped).Sum(x => x.QtyShipped);
                reserved = movements.Where(x => x.SalesInvoiceLineId == line.Id && x.Status == ShipmentStatus.Pending).Sum(x => x.QtyShipped);
                remaining = Math.Max(0, line.QuantityInvoiced - shipped - reserved - invoiceCredits.GetValueOrDefault(line.Id));
            }
            if (remaining > UomConversion.QuantityTolerance)
                result.Add(new(line.SalesInvoice!.Id, line.SalesInvoice.InvoiceNumber, line.Id, line.ItemId!.Value, line.Item!.Name,
                    line.UomId, line.UomName, line.UomConversionFactor, line.QuantityInvoiced, shipped, reserved, remaining, line.DimensionSetId));
        }
        return result.OrderBy(x => x.OrderNumber).ThenBy(x => x.ItemName).ToList();
    }

    private static async Task<List<ShipmentOrderCandidate>> GetCandidatesAsync(AppDbContext db, Guid companyId, Guid customerId)
    {
        var orders = await db.SalesOrders.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Item)
            .Where(x => x.CompanyId == companyId && x.CustomerId == customerId && !x.IsDirectInvoice && !x.InvoiceBatchId.HasValue && (x.Status == OrderStatus.Order || x.Status == OrderStatus.Confirmed || x.Status == OrderStatus.PartiallyShipped || x.Status == OrderStatus.PartiallyInvoiced || x.Status == OrderStatus.Invoiced)).ToListAsync();
        var ids = orders.SelectMany(x => x.Lines).Select(x => x.Id).ToList();
        var movements = await db.SalesShipmentLines.AsNoTracking().Where(x => x.SalesOrderLineId.HasValue && ids.Contains(x.SalesOrderLineId.Value) && x.Shipment!.CompanyId == companyId && (x.Shipment.Status == ShipmentStatus.Pending || x.Shipment.Status == ShipmentStatus.Shipped))
            .Select(x => new { x.SalesOrderLineId, x.QtyShipped, x.Shipment!.Status }).ToListAsync();
        var credits = await InvoiceCorrectionBalances.SalesOrderAdjustmentsAsync(db, ids);
        var result = new List<ShipmentOrderCandidate>();
        foreach (var order in orders)
            foreach (var line in order.Lines.Where(x => x.Item != null && !x.Item.IsService))
            {
                var shipped = Math.Max(line.QtyShipped, movements.Where(x => x.SalesOrderLineId == line.Id && x.Status == ShipmentStatus.Shipped).Sum(x => x.QtyShipped));
                var reserved = movements.Where(x => x.SalesOrderLineId == line.Id && x.Status == ShipmentStatus.Pending).Sum(x => x.QtyShipped);
                var remaining = Math.Max(0m, line.Quantity - shipped - credits.GetValueOrDefault(line.Id) - reserved);
                if (remaining > UomConversion.QuantityTolerance) result.Add(new(order.Id, order.OrderNumber, line.Id, line.ItemId!.Value, line.Item!.Name, line.UomId, line.UomName, line.UomConversionFactor, line.Quantity, shipped, reserved, remaining, line.DimensionSetId));
            }
        return result.OrderBy(x => x.OrderNumber).ThenBy(x => x.ItemName).ToList();
    }

    public async Task<(string? Error, Guid? Id)> CreateOrderDraftAsync(Guid companyId, Guid customerId, Guid warehouseId, IReadOnlyCollection<ShipmentDraftLineInput> inputs)
    {
        if (companyId == Guid.Empty || customerId == Guid.Empty || warehouseId == Guid.Empty) return ("Select a customer and warehouse.", null);
        if (inputs.Count == 0 || inputs.Any(x => x.QuantityBase <= 0) || inputs.Select(x => x.OrderLineId).Distinct().Count() != inputs.Count) return ("Enter a positive quantity for each selected line.", null);
        await using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId && x.CompanyId == companyId && (x.Type == WarehouseType.Sales || x.Type == WarehouseType.Both))) return ("The warehouse is not available for sales.", null);
        var requestedIds = inputs.Select(x => x.OrderLineId).ToList();
        await db.SalesOrderLines.FromSqlRaw("SELECT * FROM [SalesOrderLines] WITH (UPDLOCK, HOLDLOCK)")
            .Where(x => requestedIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        var candidates = (await GetCandidatesAsync(db, companyId, customerId)).ToDictionary(x => x.LineId);
        if (inputs.Any(x => !candidates.ContainsKey(x.OrderLineId))) return ("A selected order line is no longer available. Refresh the form.", null);
        var conversionMap = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
        var itemIds = inputs.Select(x => candidates[x.OrderLineId].ItemId).Distinct().ToList();
        var items = await db.Items.Include(x => x.UomConversions).Where(x => x.CompanyId == companyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var validUoms = await db.UnitOfMeasures.Where(x => x.CompanyId == companyId).Select(x => x.Id).ToHashSetAsync();
        var itemFactors = await db.ItemUomConversionLines.AsNoTracking().Where(x => x.IsActive && itemIds.Contains(x.ItemId))
            .GroupBy(x => new { x.ItemId, x.UomId }).ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
        foreach (var input in inputs)
        {
            var candidate = candidates[input.OrderLineId]; var item = items[candidate.ItemId];
            if (UomConversion.Exceeds(input.QuantityBase, candidate.Remaining)) return ($"{item.Name}: maximum remaining is {candidate.Remaining:G29} base units.", null);
            if (input.UomId.HasValue && (!validUoms.Contains(input.UomId.Value) || (input.UomId != item.UomId && !itemFactors.ContainsKey((item.Id, input.UomId.Value)) && !UomConversion.IsConfigured(item, input.UomId)))) return ($"The selected UOM for {item.Name} is unavailable.", null);
            var factorsForItem = itemFactors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
            if (input.UomConversionFactor <= 0 || Math.Abs(input.UomConversionFactor - UomConversion.FactorFor(item, input.UomId, factorsForItem, conversionMap)) > 0.000000000000001m) return ($"The UOM conversion for {item.Name} has changed. Refresh the form.", null);
        }
        inputs = inputs.Select(x => x with { QuantityBase = UomConversion.WithinRemaining(x.QuantityBase, candidates[x.OrderLineId].Remaining) }).ToList();
        var stockReservations = await db.SalesShipmentLines.AsNoTracking()
            .Where(x => itemIds.Contains(x.ItemId) && x.Shipment!.CompanyId == companyId
                && x.Shipment.WarehouseId == warehouseId && x.Shipment.Status == ShipmentStatus.Pending)
            .GroupBy(x => x.ItemId).Select(x => new { Id = x.Key, Quantity = x.Sum(l => l.QtyShipped) })
            .ToDictionaryAsync(x => x.Id, x => x.Quantity);
        foreach (var group in inputs.GroupBy(x => candidates[x.OrderLineId].ItemId))
        {
            var onHand = await db.StockLedgers.Where(x => x.CompanyId == companyId && x.ItemId == group.Key && x.WarehouseId == warehouseId).SumAsync(x => x.QuantityChanged);
            var available = Math.Max(0m, onHand - stockReservations.GetValueOrDefault(group.Key));
            if (UomConversion.Exceeds(group.Sum(x => x.QuantityBase), available)) return ($"Insufficient stock for '{items[group.Key].Name}': {available:G29} available in the selected warehouse after other shipment drafts.", null);
        }
        var number = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AR_Shipment);
        var orderIds = inputs.Select(x => candidates[x.OrderLineId].OrderId).Distinct().ToList();
        var shipment = new SalesShipment { CompanyId = companyId, SalesOrderId = orderIds.Count == 1 ? orderIds[0] : null, WarehouseId = warehouseId, ShipmentNumber = number, Status = ShipmentStatus.Pending,
            Lines = inputs.Select(x => new SalesShipmentLine { DimensionSetId = x.OverrideDimensions ? x.DimensionSetId : candidates[x.OrderLineId].DimensionSetId, SalesOrderLineId = x.OrderLineId, ItemId = candidates[x.OrderLineId].ItemId, QtyOrdered = candidates[x.OrderLineId].Ordered, QtyShipped = x.QuantityBase, UomId = x.UomId, UomName = x.UomName, UomConversionFactor = x.UomConversionFactor }).ToList() };
        db.SalesShipments.Add(shipment); await db.SaveChangesAsync(); await tx.CommitAsync(); return (null, shipment.Id);
        }
        catch (DbUpdateException)
        {
            return ("The shipment could not be saved. Refresh the available lines and try again.", null);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return ("The shipment could not be saved because the database was busy or unavailable. Refresh the available lines and try again.", null);
        }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex))
        {
            return ("The shipment could not be saved because another transaction is using the same stock or lines. Refresh and try again.", null);
        }
    }

    public async Task<(string? Error, Guid? Id)> CreateInvoiceDraftAsync(Guid companyId, Guid customerId, Guid warehouseId, IReadOnlyCollection<ShipmentDraftLineInput> inputs)
    {
        if (companyId == Guid.Empty || customerId == Guid.Empty || warehouseId == Guid.Empty) return ("Select a customer and warehouse.", null);
        if (inputs.Count == 0 || inputs.Any(x => x.QuantityBase <= 0) || inputs.Select(x => x.OrderLineId).Distinct().Count() != inputs.Count)
            return ("Enter a positive quantity for each selected invoice line.", null);
        await using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId && x.CompanyId == companyId && (x.Type == WarehouseType.Sales || x.Type == WarehouseType.Both))) return ("The warehouse is not available for sales.", null);
            var ids = inputs.Select(x => x.OrderLineId).ToList();
            await db.SalesInvoiceLines.FromSqlRaw("SELECT * FROM [SalesInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var roots = await db.SalesInvoiceLines.Where(x => ids.Contains(x.Id) && x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).ToListAsync();
            if (roots.Distinct().Count() != roots.Count) return ("Select at most one invoice line for each sales order line.", null);
            await db.SalesOrderLines.FromSqlRaw("SELECT * FROM [SalesOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => roots.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var sourceLines = await db.SalesInvoiceLines.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var candidates = (await GetInvoiceCandidatesAsync(db, companyId, customerId, null)).ToDictionary(x => x.LineId);
            if (inputs.Any(x => !candidates.ContainsKey(x.OrderLineId))) return ("A selected invoice line is no longer available. Refresh the form.", null);
            var itemIds = inputs.Select(x => candidates[x.OrderLineId].ItemId).Distinct().ToList();
            var items = await db.Items.Include(x => x.UomConversions).Where(x => x.CompanyId == companyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var validUoms = await db.UnitOfMeasures.Where(x => x.CompanyId == companyId).Select(x => x.Id).ToHashSetAsync();
            var rules = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var itemFactors = await db.ItemUomConversionLines.AsNoTracking().Where(x => x.IsActive && itemIds.Contains(x.ItemId))
                .GroupBy(x => new { x.ItemId, x.UomId }).ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
            foreach (var input in inputs)
            {
                var candidate = candidates[input.OrderLineId]; var item = items[candidate.ItemId];
                if (UomConversion.Exceeds(input.QuantityBase, candidate.Remaining)) return ($"{item.Name}: maximum remaining is {candidate.Remaining:G29} base units.", null);
                var factors = itemFactors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                if (input.UomId.HasValue && (!validUoms.Contains(input.UomId.Value) || (input.UomId != item.UomId && !factors.ContainsKey(input.UomId.Value) && !UomConversion.IsConfigured(item, input.UomId)))) return ($"The selected UOM for {item.Name} is unavailable.", null);
                if (input.UomConversionFactor <= 0 || Math.Abs(input.UomConversionFactor - UomConversion.FactorFor(item, input.UomId, factors, rules)) > 0.000000000000001m) return ($"The UOM conversion for {item.Name} has changed. Refresh the form.", null);
            }
            inputs = inputs.Select(x => x with { QuantityBase = UomConversion.WithinRemaining(x.QuantityBase, candidates[x.OrderLineId].Remaining) }).ToList();
            var held = await db.SalesShipmentLines.AsNoTracking().Where(x => itemIds.Contains(x.ItemId) && x.Shipment!.CompanyId == companyId && x.Shipment.WarehouseId == warehouseId && x.Shipment.Status == ShipmentStatus.Pending)
                .GroupBy(x => x.ItemId).Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.QtyShipped) }).ToDictionaryAsync(x => x.Id, x => x.Quantity);
            foreach (var group in inputs.GroupBy(x => candidates[x.OrderLineId].ItemId))
            {
                var stock = await db.StockLedgers.Where(x => x.CompanyId == companyId && x.ItemId == group.Key && x.WarehouseId == warehouseId).SumAsync(x => x.QuantityChanged);
                var available = Math.Max(0, stock - held.GetValueOrDefault(group.Key));
                if (UomConversion.Exceeds(group.Sum(x => x.QuantityBase), available)) return ($"Insufficient stock for '{items[group.Key].Name}': {available:G29} available in the selected warehouse after other shipment drafts.", null);
            }
            var number = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AR_Shipment);
            var shipment = new SalesShipment { CompanyId = companyId, WarehouseId = warehouseId, ShipmentNumber = number, Status = ShipmentStatus.Pending,
                Lines = inputs.Select(x => new SalesShipmentLine { DimensionSetId = x.OverrideDimensions ? x.DimensionSetId : sourceLines[x.OrderLineId].DimensionSetId, SalesInvoiceLineId = x.OrderLineId, SalesOrderLineId = sourceLines[x.OrderLineId].SalesOrderLineId,
                    ItemId = candidates[x.OrderLineId].ItemId, QtyOrdered = candidates[x.OrderLineId].Ordered, QtyShipped = x.QuantityBase,
                    UomId = x.UomId, UomName = x.UomName, UomConversionFactor = x.UomConversionFactor }).ToList() };
            db.SalesShipments.Add(shipment); await db.SaveChangesAsync(); await tx.CommitAsync(); return (null, shipment.Id);
        }
        catch (DbUpdateException) { return ("The shipment could not be saved. Refresh the available lines and try again.", null); }
        catch (Microsoft.Data.SqlClient.SqlException) { return ("The shipment could not be saved because the database was busy. Refresh and try again.", null); }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return ("Another transaction is using the same stock or lines. Refresh and try again.", null); }
    }

    private static bool HasTransientSqlError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 1205 || sql.Number == 1222 || sql.Number == -2)) return true;
        return false;
    }
}
