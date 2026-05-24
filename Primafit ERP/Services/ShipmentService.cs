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

        public ShipmentService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps, InventoryService invService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
        }

        public async Task<List<SalesShipment>> GetPendingShipmentsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesShipments
                .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Where(s => s.CompanyId == companyId && s.Status == ShipmentStatus.Pending)
                .OrderBy(s => s.CreatedDate)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<SalesShipment>> GetShipmentHistoryAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesShipments
                .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Where(s => s.CompanyId == companyId && s.Status == ShipmentStatus.Shipped)
                .OrderByDescending(s => s.ShippedDate)
                .AsNoTracking()
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

            return orders.Where(o => o.Lines.Any(l => l.Item != null && !l.Item.IsService && l.QtyShipped < l.Quantity)).ToList();
        }

        public async Task<string> CreateShipmentFromOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders.Include(o => o.Lines).ThenInclude(l => l.Item)
                                             .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";

            bool allShipped = order.Lines.Where(l => l.Item != null && !l.Item.IsService).All(l => l.QtyShipped >= l.Quantity);
            if (allShipped) return "All physical items for this order/invoice have already been shipped.";

            bool hasPending = await ctx.SalesShipments.AnyAsync(s => s.SalesOrderId == orderId && s.Status == ShipmentStatus.Pending);
            if (hasPending) return "A pending dispatch document already exists for this order. Please process it first.";

            var shipmentLines = new List<SalesShipmentLine>();
            foreach (var line in order.Lines)
            {
                if (line.Item != null && line.Item.IsService) continue;

                decimal remainingToShip = line.Quantity - line.QtyShipped;
                if (remainingToShip > 0)
                {
                    shipmentLines.Add(new SalesShipmentLine
                    {
                        SalesOrderLineId = line.Id,
                        ItemId = line.ItemId ?? Guid.Empty,
                        QtyOrdered = remainingToShip,
                        QtyShipped = remainingToShip
                    });
                }
            }

            if (!shipmentLines.Any()) return "No physical items remaining to ship.";

            var shipment = new SalesShipment
            {
                CompanyId = order.CompanyId,
                SalesOrderId = order.Id,
                WarehouseId = order.WarehouseId,
                ShipmentNumber = $"SHP-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}",
                Lines = shipmentLines
            };

            ctx.SalesShipments.Add(shipment);
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

                if (shipment == null) return "Shipment not found.";
                if (shipment.Status != ShipmentStatus.Pending) return "Shipment is already processed.";

                var glLines = new List<GLJournalLine>();
                bool isPartial = false;

                foreach (var inputLine in actualShippedLines)
                {
                    var dbLine = shipment.Lines.FirstOrDefault(l => l.Id == inputLine.Id);
                    if (dbLine == null || inputLine.QtyShipped <= 0) continue;

                    if (inputLine.QtyShipped > dbLine.QtyOrdered)
                        return $"Cannot ship {inputLine.QtyShipped} of {dbLine.Item.Name}. Max allowed is {dbLine.QtyOrdered}.";

                    if (inputLine.QtyShipped < dbLine.QtyOrdered) isPartial = true;

                    // Fetch FRESH tracking item directly from DB context
                    var freshItem = await ctx.Items.FindAsync(dbLine.ItemId);
                    if (freshItem == null) return $"Item tracking ID reference broken for {dbLine.ItemId}";

                    // ENHANCED: Dynamic Cost Routing Strategy Engine
                    decimal resolvedUnitCost = freshItem.CostingType switch
                    {
                        CostingMethod.WACC => freshItem.WeightedAverageCost,
                        CostingMethod.StandardCosting => freshItem.StandardCost,
                        CostingMethod.UserSpecified => freshItem.UserSpecifiedCost,
                        CostingMethod.MostRecentCost => freshItem.MostRecentCost,

                        // Fallback safely to WACC for queuing lines if batch-level tracking data isn't initialized
                        CostingMethod.FIFO => freshItem.WeightedAverageCost,
                        CostingMethod.LIFO => freshItem.WeightedAverageCost,
                        _ => freshItem.WeightedAverageCost
                    };

                    decimal currentStock = await _invService.GetStockLevel(dbLine.ItemId, shipment.WarehouseId);
                    if (currentStock < inputLine.QtyShipped)
                        return $"Fulfillment failed: Insufficient physical stock for {freshItem.Name}. Have: {currentStock}, Need: {inputLine.QtyShipped}";

                    // 1. Physical Stock Ledger Entry
                    ctx.StockLedgers.Add(new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = shipment.CompanyId,
                        ItemId = dbLine.ItemId,
                        WarehouseId = shipment.WarehouseId,
                        QuantityChanged = -inputLine.QtyShipped,
                        Type = StockMovementType.Sale,
                        CostAtTime = resolvedUnitCost, // Injected the dynamically resolved method cost
                        Reference = shipment.ShipmentNumber,
                        Date = DateTime.UtcNow
                    });

                    // 2. Financial GL Entry Balancing Pair (COGS vs Inventory Asset)
                    decimal totalCogsValue = Math.Round(inputLine.QtyShipped * resolvedUnitCost, 2);
                    if (totalCogsValue > 0)
                    {
                        if (freshItem.CostOfGoodsSoldAccountId == Guid.Empty || freshItem.InventoryAssetAccountId == Guid.Empty)
                            return $"Item '{freshItem.Name}' is missing COGS or Inventory Asset GL account mappings. Cannot post to ledger.";

                        glLines.Add(new GLJournalLine { SegCoaId = freshItem.CostOfGoodsSoldAccountId, Debit = totalCogsValue, Credit = 0, Reference = $"COGS {freshItem.Name} ({freshItem.CostingType})" });
                        glLines.Add(new GLJournalLine { SegCoaId = freshItem.InventoryAssetAccountId, Debit = 0, Credit = totalCogsValue, Reference = $"Stock Out {freshItem.Name} ({freshItem.CostingType})" });
                    }

                    // 3. Update Order Lines and Tracking Data
                    var soLine = shipment.SalesOrder.Lines.First(l => l.Id == dbLine.SalesOrderLineId);
                    soLine.QtyShipped += inputLine.QtyShipped;
                    dbLine.QtyShipped = inputLine.QtyShipped;
                }

                // Post Financial Journal Batch
                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(shipment.CompanyId, DateOnly.FromDateTime(DateTime.Today), "Shipment", $"Ship {shipment.ShipmentNumber}", glLines, userId);
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

                bool allShipped = shipment.SalesOrder.Lines.Where(l => l.Item != null && !l.Item.IsService).All(l => l.QtyShipped >= l.Quantity);

                if (shipment.SalesOrder.Status != OrderStatus.Invoiced && shipment.SalesOrder.Status != OrderStatus.PartiallyInvoiced)
                {
                    shipment.SalesOrder.Status = allShipped ? OrderStatus.Shipped : OrderStatus.PartiallyShipped;
                }

                if (isPartial)
                {
                    await CreateShipmentFromOrderAsync(shipment.SalesOrderId); // Triggers continuous backorder tracking
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