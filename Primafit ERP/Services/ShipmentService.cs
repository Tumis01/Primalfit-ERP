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
            return await ctx.SalesShipments
                .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Include(s => s.CustomTransactionType)
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

            return orders.Where(o => o.Lines.Any(l =>
            {
                if (l.Item == null || l.Item.IsService) return false;
                decimal alreadyCredited = creditedQuantitiesMap.TryGetValue(l.Id, out var cred) ? cred : 0;
                decimal remainingToShip = l.Quantity - l.QtyShipped - alreadyCredited;
                return remainingToShip > 0.001m;
            })).ToList();
        }

        public async Task<string> CreateShipmentFromOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Validation Error: Order reference not found.";

            if (order.WarehouseId == Guid.Empty)
                return $"Validation Error: Invoice '{order.OrderNumber}' does not have a fulfillment warehouse assigned.";

            var creditedQuantitiesMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status == CreditNoteStatus.Posted)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            bool hasPending = await ctx.SalesShipments.AnyAsync(s => s.SalesOrderId == orderId && s.Status == ShipmentStatus.Pending);
            if (hasPending)
                return $"Queue Alert: A pending dispatch document already exists in the queue for {order.OrderNumber}.";

            var shipmentLines = new List<SalesShipmentLine>();
            foreach (var line in order.Lines)
            {
                if (line.Item != null && line.Item.IsService) continue;

                decimal alreadyCredited = creditedQuantitiesMap.TryGetValue(line.Id, out var creditedQty) ? creditedQty : 0;
                decimal remainingToShip = line.Quantity - line.QtyShipped - alreadyCredited;

                if (remainingToShip > 0.001m)
                {
                    shipmentLines.Add(new SalesShipmentLine
                    {
                        Id = Guid.NewGuid(),
                        SalesOrderLineId = line.Id,
                        ItemId = line.ItemId ?? Guid.Empty,
                        QtyOrdered = line.Quantity - alreadyCredited,
                        QtyShipped = remainingToShip
                    });
                }
            }

            if (!shipmentLines.Any())
                return $"Validation Error: All physical items for invoice '{order.OrderNumber}' have already been dispatched or credited out.";

            var shipment = new SalesShipment
            {
                Id = Guid.NewGuid(),
                CompanyId = order.CompanyId,
                SalesOrderId = order.Id,
                WarehouseId = order.WarehouseId,
                CustomTransactionTypeId = order.CustomTransactionTypeId,
                CreatedDate = DateTime.UtcNow,
                Status = ShipmentStatus.Pending,
                ShipmentNumber = $"SHP-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}",
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

                var validLinesToShip = actualShippedLines.Where(l => l.QtyShipped > 0).ToList();
                if (!validLinesToShip.Any())
                    return "Validation Error: At least one line item must have a dispatched quantity greater than 0.";

                // Resolve custom mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (shipment.CustomTransactionTypeId.HasValue && shipment.CustomTransactionTypeId.Value != Guid.Empty)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.CompanyId == shipment.CompanyId && m.CustomTransactionTypeId == shipment.CustomTransactionTypeId.Value);
                }

                var glLines = new List<GLJournalLine>();
                bool isPartial = false;

                foreach (var inputLine in actualShippedLines)
                {
                    var dbLine = shipment.Lines.FirstOrDefault(l => l.Id == inputLine.Id);
                    if (dbLine == null || inputLine.QtyShipped <= 0) continue;

                    if (inputLine.QtyShipped > dbLine.QtyOrdered)
                        return $"Validation Error: Cannot ship {inputLine.QtyShipped:N2} of {dbLine.Item?.Name}. Max allowed balance is {dbLine.QtyOrdered:N2}.";

                    if (inputLine.QtyShipped < dbLine.QtyOrdered) isPartial = true;

                    var freshItem = await ctx.Items.FindAsync(dbLine.ItemId);
                    if (freshItem == null) return $"Product master ID reference broken for item ID {dbLine.ItemId}.";

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

                    // 1. Physical Stock Ledger Entry
                    ctx.StockLedgers.Add(new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = shipment.CompanyId,
                        ItemId = dbLine.ItemId,
                        WarehouseId = shipment.WarehouseId,
                        QuantityChanged = -inputLine.QtyShipped,
                        Type = StockMovementType.Sale,
                        CostAtTime = resolvedUnitCost,
                        Reference = shipment.ShipmentNumber,
                        Date = DateTime.UtcNow
                    });

                    // 2. Financial Ledger Routing (COGS Debit vs Inventory Asset Credit)
                    decimal totalCogsValue = Math.Round(inputLine.QtyShipped * resolvedUnitCost, 2);
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

                    // 3. Update Sales Order Line Trackers
                    var soLine = shipment.SalesOrder?.Lines.FirstOrDefault(l => l.Id == dbLine.SalesOrderLineId);
                    if (soLine != null) soLine.QtyShipped += inputLine.QtyShipped;
                    dbLine.QtyShipped = inputLine.QtyShipped;
                }

                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                        shipment.CompanyId,
                        DateOnly.FromDateTime(DateTime.Today),
                        "Shipment Dispatch",
                        $"Ship {shipment.ShipmentNumber}",
                        glLines,
                        userId);

                    if (!string.IsNullOrEmpty(err)) throw new Exception($"Journal Creation Failed: {err}");

                    if (batchId.HasValue)
                    {
                        var postErr = await _glOps.PostBatchAsync(shipment.CompanyId, batchId.Value, userId);
                        if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Engine Rejected Posting: {postErr}");

                        shipment.ShipmentBatchId = batchId;
                    }
                }

                shipment.Status = ShipmentStatus.Shipped;
                shipment.ShippedDate = DateTime.UtcNow;
                shipment.ConfirmedBy = confirmedBy;

                if (shipment.SalesOrder != null)
                {
                    bool allShipped = shipment.SalesOrder.Lines
                        .Where(l => l.Item != null && !l.Item.IsService)
                        .All(l => l.QtyShipped >= l.Quantity);

                    if (shipment.SalesOrder.Status != OrderStatus.Invoiced && shipment.SalesOrder.Status != OrderStatus.PartiallyInvoiced)
                    {
                        shipment.SalesOrder.Status = allShipped ? OrderStatus.Shipped : OrderStatus.PartiallyShipped;
                    }
                }

                if (isPartial)
                {
                    await CreateShipmentFromOrderAsync(shipment.SalesOrderId);
                }

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
    }
}