using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class ShipmentService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly InventoryService _invService;
        private readonly TransactionMappingService _mappingService;

        public ShipmentService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            InventoryService invService,
            TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService;
        }

        public async Task<List<SalesShipment>> GetPendingShipmentsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Older versions automatically created the next partial-shipment draft
            // immediately after approval. Remove those legacy orphan drafts so an
            // invoice is not presented as loaded until the user explicitly loads it.
            var pendingDrafts = await ctx.SalesShipments
                .Where(s => s.CompanyId == companyId
                    && s.Status == ShipmentStatus.Pending
                    && !s.ShipmentBatchId.HasValue)
                .ToListAsync();

            if (pendingDrafts.Count > 0)
            {
                var postedShipments = await ctx.SalesShipments
                    .Where(s => s.CompanyId == companyId
                        && s.Status == ShipmentStatus.Shipped
                        && s.ShippedDate.HasValue)
                    .Select(s => new { s.SalesOrderId, ShippedDate = s.ShippedDate!.Value })
                    .ToListAsync();

                var legacyAutoDrafts = pendingDrafts
                    .Where(d => postedShipments.Any(p => p.SalesOrderId == d.SalesOrderId
                        // The old automatic draft was created immediately after
                        // finalization; user-loaded drafts are not treated as stale.
                        && d.CreatedDate >= p.ShippedDate
                        && d.CreatedDate <= p.ShippedDate.AddMinutes(1)))
                    .ToList();

                if (legacyAutoDrafts.Count > 0)
                {
                    ctx.SalesShipments.RemoveRange(legacyAutoDrafts);
                    foreach (var draft in legacyAutoDrafts)
                    {
                        ctx.AuditLogs.Add(new AuditLog
                        {
                            CompanyId = companyId,
                            UserId = "system",
                            Action = "LegacyAutoShipmentDraftRemoved",
                            EntityType = nameof(SalesShipment),
                            EntityId = draft.Id,
                            Details = $"Removed legacy automatically-created shipment draft '{draft.ShipmentNumber}'. The invoice must be loaded manually for another dispatch.",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    await ctx.SaveChangesAsync();
                }
            }

            // Reconcile any approved dispatches before loading the work queue. This
            // prevents a posted shipment from remaining visible as an open draft if
            // approval and the source-page refresh happened in different requests.
            var approvedPendingIds = await ctx.SalesShipments
                .Where(s => s.CompanyId == companyId && s.Status == ShipmentStatus.Pending
                    && s.ShipmentBatchId.HasValue
                    && ctx.GLBatches.Any(b => b.Id == s.ShipmentBatchId.Value
                        && b.CompanyId == companyId && b.Status == BatchStatus.Posted))
                .Select(s => s.Id)
                .ToListAsync();

            foreach (var shipmentId in approvedPendingIds)
                await FinalizeApprovedShipmentAsync(shipmentId, "system");

            return await ctx.SalesShipments
                .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Include(s => s.CustomTransactionType)
                .Where(s => s.CompanyId == companyId && s.Status == ShipmentStatus.Pending
                    && (!s.ShipmentBatchId.HasValue
                        || !ctx.GLBatches.Any(b => b.Id == s.ShipmentBatchId.Value
                            && b.CompanyId == companyId && b.Status == BatchStatus.Posted)))
                .OrderBy(s => s.CreatedDate)
                .ToListAsync();
        }

        public async Task<List<SalesShipment>> GetShipmentHistoryAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesShipments
                .AsNoTracking()
                .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Where(s => s.CompanyId == companyId && s.Status == ShipmentStatus.Shipped)
                .OrderByDescending(s => s.ShippedDate)
                .ToListAsync();
        }

        public async Task<List<SalesOrder>> GetUnshippedOrdersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var orders = await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId &&
                            (o.Status == OrderStatus.Order || o.Status == OrderStatus.Invoiced ||
                             o.Status == OrderStatus.PartiallyShipped || o.Status == OrderStatus.PartiallyInvoiced))
                .ToListAsync();

            var orderIds = orders.Select(o => o.Id).ToList();

            var creditedQuantitiesMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => orderIds.Contains(cnl.Header!.SalesOrderId) && cnl.Header.Status == CreditNoteStatus.Posted)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var refundedQuantitiesMap = await ctx.ReceiptRefundLines
                .Include(rrl => rrl.Header)
                .Where(rrl => orderIds.Contains(rrl.Header!.SalesOrderId) && rrl.Header.Status == ReceiptRefundStatus.Posted)
                .GroupBy(rrl => rrl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var postedShipmentQuantitiesMap = await GetPostedShipmentQuantitiesAsync(ctx, orderIds);

            return orders.Where(o => o.Lines.Any(l =>
            {
                if (l.Item == null || l.Item.IsService) return false;
                decimal alreadyCredited = creditedQuantitiesMap.TryGetValue(l.Id, out var cred) ? cred : 0;
                decimal alreadyRefunded = refundedQuantitiesMap.TryGetValue(l.Id, out var refQty) ? refQty : 0;

                // Effective Target = Ordered Qty - Credit Notes - Returns
                decimal effectiveTarget = Math.Max(0, l.Quantity - alreadyCredited - alreadyRefunded);
                decimal postedDispatched = postedShipmentQuantitiesMap.TryGetValue(l.Id, out var shippedQty)
                    ? shippedQty
                    : l.QtyShipped;
                decimal netDispatched = Math.Max(0, postedDispatched - alreadyRefunded);

                return (effectiveTarget - netDispatched) > 0.001m;
            })).ToList();
        }

        public async Task<string> CreateShipmentFromOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // A previous approval may have completed without the shipment page
            // refreshing. Reconcile that posted dispatch before calculating the
            // next remainder draft so it cannot block or duplicate the queue.
            var postedPendingShipmentIds = await ctx.SalesShipments
                .Where(s => s.SalesOrderId == orderId && s.Status == ShipmentStatus.Pending
                    && s.ShipmentBatchId.HasValue
                    && ctx.GLBatches.Any(b => b.Id == s.ShipmentBatchId.Value
                        && b.Status == BatchStatus.Posted))
                .Select(s => s.Id)
                .ToListAsync();

            foreach (var shipmentId in postedPendingShipmentIds)
                await FinalizeApprovedShipmentAsync(shipmentId, "system");

            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Validation Error: Order reference not found.";
            if (order.WarehouseId == Guid.Empty) return $"Validation Error: Invoice '{order.OrderNumber}' does not have a fulfillment warehouse assigned.";

            var creditedQuantitiesMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status == CreditNoteStatus.Posted)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var refundedQuantitiesMap = await ctx.ReceiptRefundLines
                .Include(rrl => rrl.Header)
                .Where(rrl => rrl.Header!.SalesOrderId == orderId && rrl.Header.Status == ReceiptRefundStatus.Posted)
                .GroupBy(rrl => rrl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var postedShipmentQuantitiesMap = await GetPostedShipmentQuantitiesAsync(ctx, new[] { orderId });

            bool hasPending = await ctx.SalesShipments.AnyAsync(s => s.SalesOrderId == orderId && s.Status == ShipmentStatus.Pending);
            if (hasPending) return $"Queue Alert: A pending dispatch document already exists in the queue for {order.OrderNumber}.";

            var shipmentLines = new List<SalesShipmentLine>();
            foreach (var line in order.Lines)
            {
                if (line.Item != null && line.Item.IsService) continue;

                decimal alreadyCredited = creditedQuantitiesMap.TryGetValue(line.Id, out var creditedQty) ? creditedQty : 0;
                decimal alreadyRefunded = refundedQuantitiesMap.TryGetValue(line.Id, out var refQty) ? refQty : 0;

                decimal effectiveTarget = Math.Max(0, line.Quantity - alreadyCredited - alreadyRefunded);
                decimal postedDispatched = postedShipmentQuantitiesMap.TryGetValue(line.Id, out var shippedQty)
                    ? shippedQty
                    : line.QtyShipped;
                decimal netDispatched = Math.Max(0, postedDispatched - alreadyRefunded);
                decimal remainingToShip = effectiveTarget - netDispatched;

                if (remainingToShip > 0.001m)
                {
                    shipmentLines.Add(new SalesShipmentLine
                    {
                        Id = Guid.NewGuid(),
                        SalesOrderLineId = line.Id,
                        ItemId = line.ItemId ?? Guid.Empty,
                        UomId = line.UomId,
                        UomName = line.UomName,
                        UomConversionFactor = line.UomConversionFactor,
                        QtyOrdered = line.Quantity - alreadyCredited,
                        QtyShipped = remainingToShip
                    });
                }
            }

            if (!shipmentLines.Any())
                return $"Validation Error: All physical items for invoice '{order.OrderNumber}' have already been dispatched or adjusted out.";

            var shipment = new SalesShipment
            {
                Id = Guid.NewGuid(),
                CompanyId = order.CompanyId,
                SalesOrderId = order.Id,
                WarehouseId = order.WarehouseId,
                CustomTransactionTypeId = order.CustomTransactionTypeId,
                CreatedDate = DateTime.UtcNow,
                Status = ShipmentStatus.Pending,
                ShipmentNumber = await GenerateShipmentNumberAsync(ctx, order.CompanyId),
                Lines = shipmentLines
            };

            ctx.SalesShipments.Add(shipment);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> UpdateShipmentRouteAsync(Guid shipmentId, Guid? customTemplateId, Guid? overrideCogsId, Guid? overrideAssetId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var shipment = await ctx.SalesShipments.FindAsync(shipmentId);
            if (shipment == null) return "Shipment not found.";
            if (shipment.Status != ShipmentStatus.Pending) return "Cannot modify routing on an already dispatched shipment.";

            shipment.CustomTransactionTypeId = customTemplateId;
            shipment.OverrideCogsGlAccountId = overrideCogsId;
            shipment.OverrideInventoryAssetGlAccountId = overrideAssetId;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> PostShipmentAsync(Guid shipmentId, List<SalesShipmentLine> actualShippedLines, string confirmedBy, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                var shipment = await ctx.SalesShipments
                    .Include(s => s.SalesOrder).ThenInclude(o => o.Lines)
                    .Include(s => s.Lines).ThenInclude(l => l.Item)
                    .FirstOrDefaultAsync(s => s.Id == shipmentId);

                if (shipment == null) return "Shipment document not found.";
                if (shipment.Status != ShipmentStatus.Pending) return "Shipment document is already processed and locked.";

                if (shipment.ShipmentBatchId.HasValue)
                {
                    var existingGlBatch = await ctx.GLBatches.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == shipment.ShipmentBatchId.Value && b.CompanyId == shipment.CompanyId);
                    if (existingGlBatch?.Status == BatchStatus.Posted)
                        return "This shipment has already been posted to the General Ledger.";
                    if (existingGlBatch?.Status == BatchStatus.Ready)
                        return "This shipment is already awaiting GL review.";
                }

                if (actualShippedLines == null || actualShippedLines.Count == 0)
                    return "Validation Error: At least one shipment line is required.";
                if (actualShippedLines.Any(l => l.Id == Guid.Empty))
                    return "Validation Error: One or more shipment lines are missing their line identifier.";
                if (actualShippedLines.Any(l => l.QtyShipped < 0))
                    return "Validation Error: Shipped quantities cannot be negative.";
                if (actualShippedLines.GroupBy(l => l.Id).Any(g => g.Count() > 1))
                    return "Validation Error: A shipment line cannot be submitted more than once.";
                if (actualShippedLines.Any(l => shipment.Lines.All(dbLine => dbLine.Id != l.Id)))
                    return "Validation Error: One or more submitted lines do not belong to this shipment.";
                if (actualShippedLines.Any(l => shipment.Lines.First(dbLine => dbLine.Id == l.Id).ItemId != l.ItemId))
                    return "Validation Error: One or more submitted lines do not match the shipment item.";

                var validLinesToShip = actualShippedLines.Where(l => l.QtyShipped > 0).ToList();
                if (!validLinesToShip.Any())
                    return "Validation Error: At least one line item must have a dispatched quantity greater than 0.";

                var postedShipmentQuantitiesMap = await GetPostedShipmentQuantitiesAsync(ctx, new[] { shipment.SalesOrderId });

                // Resolve custom mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (shipment.CustomTransactionTypeId.HasValue && shipment.CustomTransactionTypeId.Value != Guid.Empty)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.CompanyId == shipment.CompanyId && m.CustomTransactionTypeId == shipment.CustomTransactionTypeId.Value);
                }

                var glLines = new List<GLJournalLine>();
                foreach (var itemGroup in validLinesToShip.GroupBy(l => l.ItemId))
                {
                    var item = await ctx.Items.FindAsync(itemGroup.Key);
                    if (item == null) return $"Product master ID reference broken for item ID {itemGroup.Key}.";
                    var requestedQuantity = itemGroup.Sum(l => l.QtyShipped);
                    var availableStock = await _invService.GetStockLevel(itemGroup.Key, shipment.WarehouseId);
                    if (availableStock < requestedQuantity)
                        return $"Fulfillment Refused: Insufficient stock for '{item.Name}'. Warehouse currently has {availableStock:N2}, requested dispatch is {requestedQuantity:N2}.";
                }

                foreach (var inputLine in actualShippedLines)
                {
                    var dbLine = shipment.Lines.FirstOrDefault(l => l.Id == inputLine.Id);
                    if (dbLine == null || inputLine.QtyShipped <= 0) continue;

                    var orderLine = shipment.SalesOrder?.Lines.FirstOrDefault(l => l.Id == dbLine.SalesOrderLineId);
                    var previouslyShipped = Math.Max(0, orderLine?.QtyShipped ?? 0);
                    var postedPreviouslyShipped = postedShipmentQuantitiesMap.TryGetValue(dbLine.SalesOrderLineId, out var postedQty)
                        ? postedQty
                        : previouslyShipped;
                    var maxLeftToShip = Math.Max(0, dbLine.QtyOrdered - postedPreviouslyShipped);
                    if (inputLine.QtyShipped > maxLeftToShip)
                        return $"Validation Error: Cannot ship {inputLine.QtyShipped:N2} of {dbLine.Item?.Name}. Previously shipped: {postedPreviouslyShipped:N2}; maximum left to ship: {maxLeftToShip:N2}.";

                    var freshItem = await ctx.Items.FindAsync(dbLine.ItemId);
                    if (freshItem == null) return $"Product master ID reference broken for item ID {dbLine.ItemId}.";

                    // QtyShipped is normalized/base quantity. Retain and validate the
                    // selected UOM snapshot for accurate dispatch history.
                    decimal expectedFactor = UomConversion.FactorFor(freshItem, inputLine.UomId);
                    if (inputLine.UomId.HasValue && Math.Abs(UomConversion.NormalizeFactor(inputLine.UomConversionFactor) - expectedFactor) > 0.0001m)
                        return $"Validation Error: The selected UOM conversion for '{freshItem.Name}' is invalid or outdated. Reload the shipment and try again.";

                    dbLine.UomId = inputLine.UomId;
                    dbLine.UomName = string.IsNullOrWhiteSpace(inputLine.UomName)
                        ? UomConversion.NameFor(freshItem, inputLine.UomId)
                        : inputLine.UomName.Trim();
                    dbLine.UomConversionFactor = expectedFactor;

                    decimal resolvedUnitCost = freshItem.CostingType switch
                    {
                        CostingMethod.WACC => freshItem.WeightedAverageCost,
                        CostingMethod.StandardCosting => freshItem.StandardCost,
                        CostingMethod.UserSpecified => freshItem.UserSpecifiedCost,
                        CostingMethod.MostRecentCost => freshItem.MostRecentCost,
                        _ => freshItem.WeightedAverageCost
                    };

                    decimal currentStock = await _invService.GetStockLevel(dbLine.ItemId, shipment.WarehouseId);
                    if (currentStock < inputLine.QtyShipped)
                        return $"Fulfillment Refused: Insufficient stock for '{freshItem.Name}'. Warehouse currently has {currentStock:N2}, requested dispatch is {inputLine.QtyShipped:N2}.";

                    // Stage the financial routing only. Stock and sales-order quantities
                    // are committed by FinalizeApprovedShipmentAsync after GL approval.
                    decimal totalCogsValue = Math.Round(inputLine.QtyShipped * resolvedUnitCost, 4);
                    if (totalCogsValue > 0)
                    {
                        Guid cogsAccountId = shipment.OverrideCogsGlAccountId
                            ?? customMapping?.OverrideDebitGlAccountId
                            ?? await _mappingService.GetMappedAccountAsync(
                                shipment.CompanyId,
                                SystemTransactionType.ShipmentDispatch,
                                isDebit: true,
                                defaultAccountId: freshItem.CostOfGoodsSoldAccountId);

                        Guid inventoryAssetAccountId = shipment.OverrideInventoryAssetGlAccountId
                            ?? customMapping?.OverrideCreditGlAccountId
                            ?? await _mappingService.GetMappedAccountAsync(
                                shipment.CompanyId,
                                SystemTransactionType.ShipmentDispatch,
                                isDebit: false,
                                defaultAccountId: freshItem.InventoryAssetAccountId);

                        if (cogsAccountId == Guid.Empty || inventoryAssetAccountId == Guid.Empty)
                            return $"Configuration Error: Item '{freshItem.Name}' is missing COGS or Inventory Asset GL account mappings.";

                        glLines.Add(new GLJournalLine { SegCoaId = cogsAccountId, Debit = totalCogsValue, Credit = 0, Reference = $"COGS: {freshItem.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = inventoryAssetAccountId, Debit = 0, Credit = totalCogsValue, Reference = $"Stock Out: {freshItem.Name}" });
                    }

                    // Retain the requested quantity on the pending shipment for reviewer visibility.
                    dbLine.QtyShipped = inputLine.QtyShipped;
                }

                if (!glLines.Any())
                    return "Validation Error: No financial lines were generated for this shipment. Check item costing and GL mappings.";

                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                        shipment.CompanyId,
                        DateOnly.FromDateTime(DateTime.Today),
                        "Shipment Dispatch",
                        $"Ship {shipment.ShipmentNumber}",
                        glLines,
                        userId,
                        existingBatchId: shipment.ShipmentBatchId);

                    if (!string.IsNullOrEmpty(err)) throw new Exception($"Journal Creation Failed: {err}");

                    if (batchId.HasValue)
                    {
                        var postErr = await _glOps.PostBatchAsync(shipment.CompanyId, batchId.Value, userId);
                        if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Engine Rejected Posting: {postErr}");

                        shipment.ShipmentBatchId = batchId;
                    }
                }

                shipment.ConfirmedBy = confirmedBy;

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Shipment Error: {ex.Message}";
            }
        }

        public async Task<string> FinalizeApprovedShipmentAsync(Guid shipmentId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var shipment = await ctx.SalesShipments
                .Include(s => s.SalesOrder).ThenInclude(o => o.Lines)
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(s => s.Id == shipmentId);

            if (shipment == null) return "Shipment document not found.";
            if (shipment.Status == ShipmentStatus.Shipped) return string.Empty;
            if (!shipment.ShipmentBatchId.HasValue) return "Shipment has no financial review batch.";

            var batch = await ctx.GLBatches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == shipment.ShipmentBatchId.Value && b.CompanyId == shipment.CompanyId);
            if (batch?.Status != BatchStatus.Posted) return "Shipment financial batch has not been approved and posted.";

            foreach (var line in shipment.Lines.Where(l => l.QtyShipped > 0))
            {
                var orderLine = shipment.SalesOrder?.Lines.FirstOrDefault(l => l.Id == line.SalesOrderLineId);
                var item = line.Item ?? await ctx.Items.FindAsync(line.ItemId);
                if (item == null) return $"Product master ID reference broken for item ID {line.ItemId}.";
                var currentStock = await _invService.GetStockLevel(line.ItemId, shipment.WarehouseId);
                if (currentStock < line.QtyShipped)
                    return $"Fulfillment refused after approval: insufficient stock for '{item.Name}'.";

                decimal unitCost = item.CostingType switch
                {
                    CostingMethod.WACC => item.WeightedAverageCost,
                    CostingMethod.StandardCosting => item.StandardCost,
                    CostingMethod.UserSpecified => item.UserSpecifiedCost,
                    CostingMethod.MostRecentCost => item.MostRecentCost,
                    _ => item.WeightedAverageCost
                };
                ctx.StockLedgers.Add(new StockLedger
                {
                    Id = Guid.NewGuid(), CompanyId = shipment.CompanyId, ItemId = line.ItemId,
                    WarehouseId = shipment.WarehouseId, QuantityChanged = -line.QtyShipped,
                    UomId = line.UomId,
                    UomName = string.IsNullOrWhiteSpace(line.UomName) ? item.UoM : line.UomName,
                    UomConversionFactor = UomConversion.NormalizeFactor(line.UomConversionFactor),
                    QuantityInUom = UomConversion.FromBase(line.QtyShipped, line.UomConversionFactor),
                    Type = StockMovementType.Sale, CostAtTime = unitCost,
                    Reference = shipment.ShipmentNumber, Date = DateTime.UtcNow
                });

                var soLine = shipment.SalesOrder?.Lines.FirstOrDefault(l => l.Id == line.SalesOrderLineId);
                if (soLine != null) soLine.QtyShipped += line.QtyShipped;
            }

            shipment.Status = ShipmentStatus.Shipped;
            shipment.ShippedDate = DateTime.UtcNow;
            if (shipment.SalesOrder != null)
            {
                bool allShipped = shipment.SalesOrder.Lines.Where(l => l.Item != null && !l.Item.IsService)
                    .All(l => l.QtyShipped >= l.Quantity);
                if (shipment.SalesOrder.Status != OrderStatus.Invoiced && shipment.SalesOrder.Status != OrderStatus.PartiallyInvoiced)
                    shipment.SalesOrder.Status = allShipped ? OrderStatus.Shipped : OrderStatus.PartiallyShipped;
            }

            ctx.AuditLogs.Add(new AuditLog
            {
                CompanyId = shipment.CompanyId,
                UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId,
                Action = "ShipmentApprovedAndFinalized",
                EntityType = nameof(SalesShipment),
                EntityId = shipment.Id,
                Details = $"Shipment '{shipment.ShipmentNumber}' was finalized after GL batch approval.",
                CreatedAt = DateTime.UtcNow
            });

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        private static async Task<string> GenerateShipmentNumberAsync(AppDbContext ctx, Guid companyId)
        {
            string shipmentNumber;
            do
            {
                shipmentNumber = $"SHP-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
            }
            while (await ctx.SalesShipments.AnyAsync(s => s.CompanyId == companyId && s.ShipmentNumber == shipmentNumber));

            return shipmentNumber;
        }

        private static async Task<Dictionary<Guid, decimal>> GetPostedShipmentQuantitiesAsync(
            AppDbContext ctx,
            IEnumerable<Guid> orderIds)
        {
            var ids = orderIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, decimal>();

            return await ctx.SalesShipments
                .AsNoTracking()
                .Where(s => ids.Contains(s.SalesOrderId) && s.Status == ShipmentStatus.Shipped)
                .SelectMany(s => s.Lines)
                .GroupBy(line => line.SalesOrderLineId)
                .ToDictionaryAsync(group => group.Key, group => group.Sum(line => line.QtyShipped));
        }
    }
}
