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
        private readonly IDocumentNumberingService _numberingService;

        public ShipmentService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            InventoryService invService,
            TransactionMappingService mappingService,
            IDocumentNumberingService numberingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService;
            _numberingService = numberingService;
        }

        public async Task<List<SalesShipment>> GetPendingShipmentsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

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
                .Include(s => s.Lines).ThenInclude(l => l.SalesInvoiceLine).ThenInclude(l => l!.SalesInvoice).ThenInclude(i => i!.Customer)
                .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(l => l!.Header).ThenInclude(o => o!.Customer)
                .Include(s => s.CustomTransactionType)
                // A posted batch can still be pending if physical finalization failed
                // (for example, stock was consumed during approval). Keep it visible
                // so it can be investigated and retried instead of hiding it.
                .Where(s => s.CompanyId == companyId && s.Status == ShipmentStatus.Pending)
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
                .Include(s => s.Lines).ThenInclude(l => l.SalesInvoiceLine).ThenInclude(l => l!.SalesInvoice).ThenInclude(i => i!.Customer)
                .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(l => l!.Header).ThenInclude(o => o!.Customer)
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
                .Where(cnl => (cnl.Header!.SalesOrderId.HasValue && orderIds.Contains(cnl.Header.SalesOrderId.Value)) && cnl.Header.Status == CreditNoteStatus.Posted)
                .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var refundedQuantitiesMap = await ctx.ReceiptRefundLines
                .Include(rrl => rrl.Header)
                .Where(rrl => (rrl.Header!.SalesOrderId.HasValue && orderIds.Contains(rrl.Header.SalesOrderId.Value)) && rrl.Header.Status == ReceiptRefundStatus.Posted)
                .Where(rrl => rrl.SalesOrderLineId.HasValue).GroupBy(rrl => rrl.SalesOrderLineId!.Value)
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

            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Validation Error: Order reference not found.";
            // A previous approval may have completed without the shipment page
            // refreshing. Grouped shipment documents have a null header order,
            // so find them by their source lines as well as the legacy header link.
            var orderLineIds = order.Lines.Select(x => x.Id).ToList();
            var postedPendingShipmentIds = await ctx.SalesShipments
                .Where(s => s.CompanyId == order.CompanyId && s.Status == ShipmentStatus.Pending
                    && (s.SalesOrderId == orderId || s.Lines.Any(l => l.SalesOrderLineId.HasValue && orderLineIds.Contains(l.SalesOrderLineId.Value)))
                    && s.ShipmentBatchId.HasValue
                    && ctx.GLBatches.Any(b => b.Id == s.ShipmentBatchId.Value
                        && b.CompanyId == order.CompanyId && b.Status == BatchStatus.Posted))
                .Select(s => s.Id)
                .ToListAsync();
            foreach (var shipmentId in postedPendingShipmentIds)
                await FinalizeApprovedShipmentAsync(shipmentId, "system");
            if (order.WarehouseId == Guid.Empty) return $"Validation Error: Invoice '{order.OrderNumber}' does not have a fulfillment warehouse assigned.";
            if (!await ctx.Warehouses.AnyAsync(w => w.Id == order.WarehouseId && w.CompanyId == order.CompanyId &&
                (w.Type == WarehouseType.Both || w.Type == WarehouseType.Sales)))
                return "Validation Error: The fulfillment warehouse is not available to the sales module.";

            var creditedQuantitiesMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status == CreditNoteStatus.Posted)
                .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var refundedQuantitiesMap = await ctx.ReceiptRefundLines
                .Include(rrl => rrl.Header)
                .Where(rrl => rrl.Header!.SalesOrderId == orderId && rrl.Header.Status == ReceiptRefundStatus.Posted)
                .Where(rrl => rrl.SalesOrderLineId.HasValue).GroupBy(rrl => rrl.SalesOrderLineId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var postedShipmentQuantitiesMap = await GetPostedShipmentQuantitiesAsync(ctx, new[] { orderId });

            bool hasPending = await ctx.SalesShipments.AnyAsync(s => s.CompanyId == order.CompanyId
                && s.Status == ShipmentStatus.Pending
                && (s.SalesOrderId == orderId || s.Lines.Any(l => l.SalesOrderLineId.HasValue && orderLineIds.Contains(l.SalesOrderLineId.Value))));
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
                    { DimensionSetId = line.DimensionSetId,
                        Id = Guid.NewGuid(),
                        SalesOrderLineId = line.Id,
                        ItemId = line.ItemId ?? Guid.Empty,
                        // Shipment quantities are stored in the item's primary
                        // (internal stock) UOM. The operator can choose another
                        // display UOM on the shipment page before submitting.
                        UomId = line.Item?.UomId,
                        UomName = line.Item?.PrimaryUom?.Name ?? line.Item?.UoM ?? "primary",
                        UomConversionFactor = 1m,
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

        public async Task<string> CreateShipmentFromOrderLinesAsync(IEnumerable<Guid> orderLineIds)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var selectedIds = orderLineIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (selectedIds.Count == 0) return "Select at least one sales order line.";

            var lines = await ctx.SalesOrderLines
                .Include(l => l.Item)
                .Include(l => l.Header).ThenInclude(h => h!.Customer)
                .Where(l => selectedIds.Contains(l.Id))
                .ToListAsync();
            if (lines.Count != selectedIds.Count) return "One or more selected sales order lines no longer exist.";
            if (lines.Any(l => l.Header == null || l.Header.CompanyId != lines[0].Header!.CompanyId))
                return "A shipment can only include sales orders from the same company.";

            await using var transaction = await ctx.Database.BeginTransactionAsync();
            var selectedGroups = lines.GroupBy(l => (l.Header!.CustomerId, l.Header.WarehouseId, l.Header.CurrencyId)).ToList();
            foreach (var selectedGroup in selectedGroups)
            {
            var groupLines = selectedGroup.ToList();
            var orders = groupLines.Select(l => l.Header!).DistinctBy(o => o.Id).ToList();
            if (orders[0].WarehouseId == Guid.Empty)
                return "A shipment needs a fulfillment site/warehouse.";
            if (!await ctx.Warehouses.AnyAsync(w => w.Id == orders[0].WarehouseId && w.CompanyId == orders[0].CompanyId
                && (w.Type == WarehouseType.Both || w.Type == WarehouseType.Sales)))
                return "The fulfillment warehouse is not available to the sales module.";
            if (orders.Any(o => o.Status is OrderStatus.Draft or OrderStatus.Quote or OrderStatus.Cancelled))
                return "Only approved sales orders can be shipped.";
            if (groupLines.Any(l => l.Item == null || l.Item.IsService))
                return "Service lines do not require a warehouse shipment.";

            var selectedLineIdSet = groupLines.Select(l => l.Id).ToList();
            if (await ctx.SalesShipments.AnyAsync(s => s.CompanyId == orders[0].CompanyId
                && s.Status == ShipmentStatus.Pending
                && s.Lines.Any(l => l.SalesOrderLineId.HasValue && selectedLineIdSet.Contains(l.SalesOrderLineId.Value))))
                return "At least one selected sales order line is already in a pending shipment.";

            var orderIds = orders.Select(o => o.Id).ToList();
            var posted = await GetPostedShipmentQuantitiesAsync(ctx, orderIds);
            var credited = await ctx.CreditNoteLines.Include(l => l.Header)
                .Where(l => (l.Header!.SalesOrderId.HasValue && orderIds.Contains(l.Header.SalesOrderId.Value)) && l.Header.Status == CreditNoteStatus.Posted
                    && (l.SalesOrderLineId.HasValue && selectedLineIdSet.Contains(l.SalesOrderLineId.Value)))
                .Where(l => l.SalesOrderLineId.HasValue).GroupBy(l => l.SalesOrderLineId!.Value).ToDictionaryAsync(g => g.Key, g => g.Sum(l => l.Quantity));
            var refunded = await ctx.ReceiptRefundLines.Include(l => l.Header)
                .Where(l => (l.Header!.SalesOrderId.HasValue && orderIds.Contains(l.Header.SalesOrderId.Value)) && l.Header.Status == ReceiptRefundStatus.Posted
                    && (l.SalesOrderLineId.HasValue && selectedLineIdSet.Contains(l.SalesOrderLineId.Value)))
                .Where(l => l.SalesOrderLineId.HasValue).GroupBy(l => l.SalesOrderLineId!.Value).ToDictionaryAsync(g => g.Key, g => g.Sum(l => l.Quantity));

            var shipmentLines = new List<SalesShipmentLine>();
            foreach (var line in groupLines)
            {
                var alreadyCredited = credited.GetValueOrDefault(line.Id);
                var alreadyRefunded = refunded.GetValueOrDefault(line.Id);
                var shipped = posted.GetValueOrDefault(line.Id, line.QtyShipped);
                var remaining = Math.Max(0, line.Quantity - alreadyCredited - alreadyRefunded - Math.Max(0, shipped - alreadyRefunded));
                if (remaining <= 0.001m) continue;
                shipmentLines.Add(new SalesShipmentLine
                { DimensionSetId = line.DimensionSetId,
                    Id = Guid.NewGuid(), SalesOrderLineId = line.Id, ItemId = line.ItemId ?? Guid.Empty,
                    UomId = line.Item?.UomId, UomName = line.Item?.PrimaryUom?.Name ?? line.Item?.UoM ?? "primary",
                    UomConversionFactor = 1m, QtyOrdered = Math.Max(0, line.Quantity - alreadyCredited - alreadyRefunded),
                    QtyShipped = remaining
                });
            }
            if (shipmentLines.Count == 0) return "The selected sales order lines have no remaining quantity to ship.";

            var shipment = new SalesShipment
            {
                Id = Guid.NewGuid(), CompanyId = orders[0].CompanyId,
                SalesOrderId = orders.Count == 1 ? orders[0].Id : null,
                WarehouseId = orders[0].WarehouseId, CustomTransactionTypeId = orders.Select(o => o.CustomTransactionTypeId).Distinct().Count() == 1
                    ? orders[0].CustomTransactionTypeId : null,
                CreatedDate = DateTime.UtcNow, Status = ShipmentStatus.Pending,
                ShipmentNumber = await GenerateShipmentNumberAsync(ctx, orders[0].CompanyId), Lines = shipmentLines
            };
            ctx.SalesShipments.Add(shipment);
            await ctx.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return string.Empty;
        }

        public async Task<string> DeleteDraftShipmentAsync(Guid companyId, Guid shipmentId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                var shipment = await ctx.SalesShipments
                    .Include(s => s.Lines)
                    .FirstOrDefaultAsync(s => s.Id == shipmentId && s.CompanyId == companyId);

                if (shipment == null) return "Shipment draft not found.";
                if (shipment.Status != ShipmentStatus.Pending)
                    return "Only an unshipped draft can be deleted.";

                GLBatch? linkedBatch = null;
                if (shipment.ShipmentBatchId.HasValue)
                {
                    linkedBatch = await ctx.GLBatches
                        .Include(b => b.Journals).ThenInclude(j => j.Lines)
                        .FirstOrDefaultAsync(b => b.Id == shipment.ShipmentBatchId.Value && b.CompanyId == companyId);

                    if (linkedBatch == null) return "The linked GL batch could not be found; the shipment was not deleted.";
                    if (linkedBatch.Status != BatchStatus.Draft || linkedBatch.IsLocked)
                        return "This shipment has been submitted for review and can no longer be deleted as a draft.";

                    if (await ctx.SalesShipments.AnyAsync(s => s.Id != shipmentId && s.ShipmentBatchId == linkedBatch.Id))
                        return "The linked GL batch is used by another shipment and cannot be deleted.";

                    ctx.GLBatches.Remove(linkedBatch);
                }

                ctx.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyId,
                    UserId = userId,
                    Action = "ShipmentDraftDeleted",
                    EntityType = nameof(SalesShipment),
                    EntityId = shipment.Id,
                    Details = $"Deleted unsubmitted shipment draft '{shipment.ShipmentNumber}'.",
                    CreatedAt = DateTime.UtcNow
                });

                ctx.SalesShipments.Remove(shipment);
                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Shipment draft could not be deleted: {ex.Message}";
            }
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
            using var transaction = await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            try
            {
                // Serialize submission and draft deletion for this shipment. The GL
                // batch and shipment reference are saved in the same transaction.
                await ctx.SalesShipments.FromSqlRaw("SELECT * FROM [SalesShipments] WITH (UPDLOCK, HOLDLOCK)")
                    .Where(s => s.Id == shipmentId).Select(s => s.Id).ToListAsync();
                var shipment = await ctx.SalesShipments
                    .Include(s => s.Lines).ThenInclude(l => l.Item)
                    .Include(s => s.Lines).ThenInclude(l => l.SalesInvoiceLine).ThenInclude(l => l!.SalesInvoice)
                    .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(l => l!.Header)
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
                if (shipment.Lines.Where(l => l.SalesOrderLineId.HasValue).Where(l => l.SalesOrderLineId.HasValue).GroupBy(l => l.SalesOrderLineId!.Value).Any(g => g.Count() > 1))
                    return "Validation Error: A sales order line cannot appear more than once on the same shipment.";
                var invoiceSource = shipment.Lines.Any(l => l.SalesInvoiceLineId.HasValue);
                if (invoiceSource ? shipment.Lines.Any(l => !l.SalesInvoiceLineId.HasValue || l.SalesInvoiceLine?.SalesInvoice == null)
                    : shipment.Lines.Any(l => l.SalesOrderLine == null))
                    return "Validation Error: Choose one valid source type for every shipment line.";
                if (invoiceSource && shipment.Lines.GroupBy(l => l.SalesInvoiceLineId).Any(g => g.Count() > 1)) return "An invoice line cannot appear twice on a shipment.";
                if (actualShippedLines.Any(l => shipment.Lines.All(dbLine => dbLine.Id != l.Id)))
                    return "Validation Error: One or more submitted lines do not belong to this shipment.";
                if (actualShippedLines.Any(l => shipment.Lines.First(dbLine => dbLine.Id == l.Id).ItemId != l.ItemId))
                    return "Validation Error: One or more submitted lines do not match the shipment item.";

                var validLinesToShip = actualShippedLines.Where(l => l.QtyShipped > 0).ToList();
                if (!validLinesToShip.Any())
                    return "Validation Error: At least one line item must have a dispatched quantity greater than 0.";

                var sourceOrders = shipment.Lines.Where(l => l.SalesOrderLine?.Header != null)
                    .Select(l => l.SalesOrderLine!.Header!).DistinctBy(o => o.Id).ToList();
                if (!invoiceSource && sourceOrders.Count == 0) return "Validation Error: Shipment lines must link to sales orders.";
                if (sourceOrders.Any(o => o.CompanyId != shipment.CompanyId || o.CustomerId != sourceOrders[0].CustomerId))
                    return "Validation Error: Shipment source orders must share the same customer and company.";
                var invoiceHeaders = shipment.Lines.Where(l => l.SalesInvoiceLine?.SalesInvoice != null).Select(l => l.SalesInvoiceLine!.SalesInvoice!).ToList();
                if (invoiceSource && (invoiceHeaders.Any(i => i.CompanyId != shipment.CompanyId || i.CustomerId != invoiceHeaders[0].CustomerId)
                    || shipment.Lines.Any(l => l.SalesInvoiceLine!.ItemId != l.ItemId || l.SalesInvoiceLine.SalesOrderLineId != l.SalesOrderLineId)))
                    return "Validation Error: Shipment invoice lines must share the same customer and match their source items.";
                var sourceOrderIds = sourceOrders.Select(o => o.Id).ToList();
                var orderLineById = shipment.Lines.Where(l => l.SalesOrderLine != null)
                    .ToDictionary(l => l.SalesOrderLineId!.Value, l => l.SalesOrderLine!);
                var postedShipmentQuantitiesMap = await GetPostedShipmentQuantitiesAsync(ctx, sourceOrderIds);
                var sourceLineIds = shipment.Lines.Where(x => x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).ToList();
                var otherReservations = await ctx.SalesShipmentLines.AsNoTracking()
                    .Where(x => x.SalesOrderLineId.HasValue && sourceLineIds.Contains(x.SalesOrderLineId.Value) && x.ShipmentId != shipment.Id && x.Shipment!.Status == ShipmentStatus.Pending && x.Shipment.CompanyId == shipment.CompanyId)
                    .GroupBy(x => x.SalesOrderLineId!.Value).Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.QtyShipped) }).ToDictionaryAsync(x => x.Id, x => x.Quantity);
                var unfulfilledCredits = await InvoiceCorrectionBalances.SalesOrderAdjustmentsAsync(ctx, sourceLineIds);
                var invoiceCandidates = invoiceSource
                    ? (await ShipmentDraftService.GetInvoiceCandidatesAsync(ctx, shipment.CompanyId, invoiceHeaders[0].CustomerId, shipment.Id)).ToDictionary(x => x.LineId)
                    : new Dictionary<Guid, ShipmentOrderCandidate>();

                var conversionMap = await ctx.UomConversionRules.AsNoTracking()
                    .Where(x => x.CompanyId == shipment.CompanyId && x.IsActive)
                    .ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
                var itemFactors = await ctx.ItemUomConversionLines.AsNoTracking()
                    .Where(x => x.IsActive && x.Item!.CompanyId == shipment.CompanyId)
                    .GroupBy(x => new { x.ItemId, x.UomId })
                    .ToDictionaryAsync(g => (g.Key.ItemId, g.Key.UomId), g => g.First().ConversionFactorToBase);

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
                    if (UomConversion.Exceeds(requestedQuantity, availableStock))
                        return $"Fulfillment Refused: Insufficient stock for '{item.Name}'. Warehouse currently has {availableStock:N2}, requested dispatch is {requestedQuantity:N2}.";
                }

                foreach (var inputLine in actualShippedLines)
                {
                    var dbLine = shipment.Lines.FirstOrDefault(l => l.Id == inputLine.Id);
                    if (dbLine == null || inputLine.QtyShipped <= 0) continue;
                    if (UomConversion.Exceeds(inputLine.QtyShipped, dbLine.QtyShipped))
                        return $"The shipment quantity for {dbLine.Item?.Name} exceeds its reserved draft quantity. Create another shipment for the additional quantity.";

                    var orderLine = dbLine.SalesOrderLineId.HasValue ? orderLineById.GetValueOrDefault(dbLine.SalesOrderLineId.Value) : null;
                    var previouslyShipped = Math.Max(0, orderLine?.QtyShipped ?? 0);
                    var postedPreviouslyShipped = dbLine.SalesOrderLineId.HasValue && postedShipmentQuantitiesMap.TryGetValue(dbLine.SalesOrderLineId.Value, out var postedQty)
                        ? postedQty
                        : previouslyShipped;
                    var root = dbLine.SalesOrderLineId.GetValueOrDefault();
                    var maxLeftToShip = invoiceSource
                        ? invoiceCandidates.GetValueOrDefault(dbLine.SalesInvoiceLineId!.Value)?.Remaining ?? 0
                        : Math.Max(0, (orderLine?.Quantity ?? dbLine.QtyOrdered) - Math.Max(previouslyShipped, postedPreviouslyShipped) - unfulfilledCredits.GetValueOrDefault(root) - otherReservations.GetValueOrDefault(root));
                    if (UomConversion.Exceeds(inputLine.QtyShipped, maxLeftToShip))
                        return $"Validation Error: Cannot ship {inputLine.QtyShipped:N2} of {dbLine.Item?.Name}. Previously shipped: {postedPreviouslyShipped:N2}; maximum left to ship: {maxLeftToShip:N2}.";
                    inputLine.QtyShipped = UomConversion.WithinRemaining(inputLine.QtyShipped, Math.Min(maxLeftToShip, dbLine.QtyShipped));

                    var freshItem = await ctx.Items.FindAsync(dbLine.ItemId);
                    if (freshItem == null) return $"Product master ID reference broken for item ID {dbLine.ItemId}.";

                    // QtyShipped is normalized to the item's primary/reference quantity. Retain and validate the
                    // selected UOM snapshot for accurate dispatch history.
                    var factorsForItem = itemFactors
                        .Where(x => x.Key.ItemId == freshItem.Id)
                        .ToDictionary(x => x.Key.UomId, x => x.Value);
                    if (inputLine.UomId.HasValue && inputLine.UomId != freshItem.UomId
                        && !factorsForItem.ContainsKey(inputLine.UomId.Value)
                        && !UomConversion.IsConfigured(freshItem, inputLine.UomId))
                        return $"Validation Error: The selected UOM for '{freshItem.Name}' is not configured.";
                    decimal expectedFactor = UomConversion.FactorFor(freshItem, inputLine.UomId, factorsForItem, conversionMap);
                    if (inputLine.UomId.HasValue && Math.Abs(UomConversion.NormalizeFactor(inputLine.UomConversionFactor) - expectedFactor) > 0.000000000000001m)
                        return $"Validation Error: The selected UOM conversion for '{freshItem.Name}' is invalid or outdated. Reload the shipment and try again.";

                    dbLine.DimensionSetId = inputLine.DimensionSetId;
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
                    if (UomConversion.Exceeds(inputLine.QtyShipped, currentStock))
                        return $"Fulfillment Refused: Insufficient stock for '{freshItem.Name}'. Warehouse currently has {currentStock:N2}, requested dispatch is {inputLine.QtyShipped:N2}.";
                    inputLine.QtyShipped = UomConversion.WithinRemaining(inputLine.QtyShipped, currentStock);

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

                        glLines.Add(new GLJournalLine { DimensionSetId = dbLine.DimensionSetId, IsDimensionDetail = true,  SegCoaId = cogsAccountId, Debit = totalCogsValue, Credit = 0, Reference = $"COGS: {freshItem.Name}" });
                        glLines.Add(new GLJournalLine { DimensionSetId = dbLine.DimensionSetId, IsDimensionDetail = true,  SegCoaId = inventoryAssetAccountId, Debit = 0, Credit = totalCogsValue, Reference = $"Stock Out: {freshItem.Name}" });
                    }

                    // Retain the requested quantity on the pending shipment for reviewer visibility.
                    dbLine.QtyShipped = inputLine.QtyShipped;
                }

                if (!glLines.Any())
                    return "Validation Error: No financial lines were generated for this shipment. Check item costing and GL mappings.";

                // Lines excluded from this submission must not retain their draft reservation:
                // finalization processes the saved quantities, not the submitted collection.
                var submittedQuantities = actualShippedLines.ToDictionary(l => l.Id, l => l.QtyShipped);
                foreach (var line in shipment.Lines)
                    if (!submittedQuantities.TryGetValue(line.Id, out var quantity) || quantity == 0)
                        line.QtyShipped = 0;

                var projects = invoiceSource ? invoiceHeaders.Select(x => x.ProjectId).Distinct().ToList()
                    : sourceOrders.Select(x => x.ProjectId).Distinct().ToList();
                var (err, batchId) = await _glOps.StageNewSubledgerBatchAsync(ctx, shipment.CompanyId,
                    DateOnly.FromDateTime(DateTime.Today), "Shipment Dispatch", $"Ship {shipment.ShipmentNumber}",
                    shipment.ShipmentNumber, glLines, userId, inheritedProjectId: projects.Count == 1 ? projects[0] : null);
                if (!string.IsNullOrEmpty(err) || !batchId.HasValue)
                    return $"Journal creation failed: {err}";
                shipment.ShipmentBatchId = batchId;

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
            await using var transaction = await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var shipment = await ctx.SalesShipments
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Include(s => s.Lines).ThenInclude(l => l.SalesInvoiceLine).ThenInclude(l => l!.SalesInvoice)
                .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(l => l!.Header)
                .FirstOrDefaultAsync(s => s.Id == shipmentId);

            if (shipment == null) return "Shipment document not found.";
            if (shipment.Status == ShipmentStatus.Shipped) return string.Empty;
            if (!shipment.ShipmentBatchId.HasValue) return "Shipment has no financial review batch.";

            var batch = await ctx.GLBatches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == shipment.ShipmentBatchId.Value && b.CompanyId == shipment.CompanyId);
            if (batch?.Status != BatchStatus.Posted) return "Shipment financial batch has not been approved and posted.";

            var sourceOrderIds = shipment.Lines.Where(l => l.SalesOrderLine?.Header != null)
                .Select(l => l.SalesOrderLine!.Header!.Id).Distinct().ToList();
            var invoiceSource = shipment.Lines.Any(l => l.SalesInvoiceLineId.HasValue);
            if ((!invoiceSource && (sourceOrderIds.Count == 0 || shipment.Lines.Any(l => l.SalesOrderLine == null)))
                || (invoiceSource && shipment.Lines.Any(l => !l.SalesInvoiceLineId.HasValue || l.SalesInvoiceLine?.SalesInvoice == null)))
                return "Shipment lines must link to valid sales orders before finalization.";
            if (invoiceSource)
            {
                var customerId = shipment.Lines.First().SalesInvoiceLine!.SalesInvoice!.CustomerId;
                var available = (await ShipmentDraftService.GetInvoiceCandidatesAsync(ctx, shipment.CompanyId, customerId, shipment.Id)).ToDictionary(x => x.LineId);
                if (shipment.Lines.Any(l => l.QtyShipped > 0 && (!available.TryGetValue(l.SalesInvoiceLineId!.Value, out var candidate) || UomConversion.Exceeds(l.QtyShipped, candidate.Remaining)
                    || candidate.ItemId != l.ItemId || l.SalesInvoiceLine!.SalesOrderLineId != l.SalesOrderLineId)))
                    return "The invoice source quantity is no longer available for this shipment.";
            }
            var sourceOrders = await ctx.SalesOrders.Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => sourceOrderIds.Contains(o.Id)).ToListAsync();
            var sourceOrderLines = sourceOrders.SelectMany(o => o.Lines).ToDictionary(l => l.Id);
            foreach (var group in shipment.Lines.Where(l => l.QtyShipped > 0).GroupBy(l => l.ItemId))
            {
                var available = await ctx.StockLedgers.Where(x => x.CompanyId == shipment.CompanyId && x.ItemId == group.Key && x.WarehouseId == shipment.WarehouseId).SumAsync(x => x.QuantityChanged);
                if (UomConversion.Exceeds(group.Sum(x => x.QtyShipped), available))
                    return $"Fulfillment refused after approval: insufficient stock for '{group.First().Item?.Name ?? group.Key.ToString()}'.";
            }
            foreach (var line in shipment.Lines.Where(l => l.QtyShipped > 0))
            {
                SalesOrderLine? orderLine = null;
                if (line.SalesOrderLineId.HasValue) sourceOrderLines.TryGetValue(line.SalesOrderLineId.Value, out orderLine);
                var item = line.Item ?? await ctx.Items.FindAsync(line.ItemId);
                if (item == null) return $"Product master ID reference broken for item ID {line.ItemId}.";

                decimal unitCost = item.CostingType switch
                {
                    CostingMethod.WACC => item.WeightedAverageCost,
                    CostingMethod.StandardCosting => item.StandardCost,
                    CostingMethod.UserSpecified => item.UserSpecifiedCost,
                    CostingMethod.MostRecentCost => item.MostRecentCost,
                    _ => item.WeightedAverageCost
                };
                ctx.StockLedgers.Add(new StockLedger
                { DimensionSetId = line.DimensionSetId,
                    Id = Guid.NewGuid(), CompanyId = shipment.CompanyId, ItemId = line.ItemId,
                    WarehouseId = shipment.WarehouseId, QuantityChanged = -line.QtyShipped,
                    UomId = line.UomId,
                    UomName = string.IsNullOrWhiteSpace(line.UomName) ? item.UoM : line.UomName,
                    UomConversionFactor = UomConversion.NormalizeFactor(line.UomConversionFactor),
                    QuantityInUom = UomConversion.FromBase(line.QtyShipped, line.UomConversionFactor),
                    Type = StockMovementType.Sale, CostAtTime = unitCost,
                    ProjectId = orderLine?.Header?.ProjectId,
                    Reference = shipment.ShipmentNumber, Date = DateTime.UtcNow,
                    GLBatchId = shipment.ShipmentBatchId
                });

                if (orderLine != null) orderLine.QtyShipped += line.QtyShipped;
            }

            shipment.Status = ShipmentStatus.Shipped;
            shipment.ShippedDate = DateTime.UtcNow;
            foreach (var order in sourceOrders)
            {
                bool allShipped = order.Lines.Where(l => l.Item != null && !l.Item.IsService)
                    .All(l => l.QtyShipped + UomConversion.QuantityTolerance >= l.Quantity);
                if (order.Status != OrderStatus.Invoiced && order.Status != OrderStatus.PartiallyInvoiced)
                    order.Status = allShipped ? OrderStatus.Shipped : OrderStatus.PartiallyShipped;
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
            await transaction.CommitAsync();
            return string.Empty;
        }

        private Task<string> GenerateShipmentNumberAsync(AppDbContext ctx, Guid companyId) =>
            _numberingService.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AR_Shipment);

        private static async Task<Dictionary<Guid, decimal>> GetPostedShipmentQuantitiesAsync(
            AppDbContext ctx,
            IEnumerable<Guid> orderIds)
        {
            var ids = orderIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, decimal>();

            return await ctx.SalesShipments
                .AsNoTracking()
                .Where(s => s.Status == ShipmentStatus.Shipped)
                .SelectMany(s => s.Lines)
                .Where(line => ctx.SalesOrderLines.Any(orderLine =>
                    orderLine.Id == line.SalesOrderLineId && ids.Contains(orderLine.HeaderId)))
                .Where(line => line.SalesOrderLineId.HasValue)
                .GroupBy(line => line.SalesOrderLineId!.Value)
                .ToDictionaryAsync(group => group.Key, group => group.Sum(line => line.QtyShipped));
        }
    }
}
