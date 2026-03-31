using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

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

        // --- NEW: Fetch shipped history ---
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

        // --- NEW: Fetch orders/invoices that still have physical goods to ship ---
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

            // Filter in memory to find orders where physical items haven't been fully shipped
            return orders.Where(o => o.Lines.Any(l => l.Item != null && !l.Item.IsService && l.QtyShipped < l.Quantity)).ToList();
        }

        public async Task<string> CreateShipmentFromOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders.Include(o => o.Lines).ThenInclude(l => l.Item)
                                             .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";

            // FIX: Removed the "Status == Invoiced" block. 
            // We now ONLY check if physical items actually need shipping.
            bool allShipped = order.Lines.Where(l => l.Item != null && !l.Item.IsService).All(l => l.QtyShipped >= l.Quantity);
            if (allShipped) return "All physical items for this order/invoice have already been shipped.";

            // Check if a pending shipment already exists for this order
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

        public async Task<string> PostShipmentAsync(Guid shipmentId, List<SalesShipmentLine> actualShippedLines, string confirmedBy)
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

                // IDEMPOTENCY: Prevent double-posting
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

                    // STRICT RULE: Fetch FRESH WAC directly from DB at the exact moment of shipment
                    var freshItem = await ctx.Items.FindAsync(dbLine.ItemId);
                    decimal currentWac = freshItem.WeightedAverageCost;

                    decimal currentStock = await _invService.GetStockLevel(dbLine.ItemId, shipment.WarehouseId);
                    if (currentStock < inputLine.QtyShipped)
                        return $"Fulfillment failed: Insufficient physical stock for {freshItem.Name}. Have: {currentStock}, Need: {inputLine.QtyShipped}";

                    // 1. Physical Ledger Entry
                    ctx.StockLedgers.Add(new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = shipment.CompanyId,
                        ItemId = dbLine.ItemId,
                        WarehouseId = shipment.WarehouseId,
                        QuantityChanged = -inputLine.QtyShipped,
                        Type = StockMovementType.Sale,
                        CostAtTime = currentWac,
                        Reference = shipment.ShipmentNumber,
                        Date = DateTime.UtcNow
                    });

                    // 2. Financial GL Entry (COGS & Inventory Asset)
                    decimal cogsValueBase = Math.Round(inputLine.QtyShipped * currentWac, 2);
                    if (cogsValueBase > 0)
                    {
                        if (freshItem.CostOfGoodsSoldAccountId == Guid.Empty || freshItem.InventoryAssetAccountId == Guid.Empty)
                            return $"Item '{freshItem.Name}' is missing COGS or Inventory Asset GL account mappings. Cannot post to ledger.";

                        glLines.Add(new GLJournalLine { SegCoaId = freshItem.CostOfGoodsSoldAccountId, Debit = cogsValueBase, Credit = 0, Reference = $"COGS {freshItem.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = freshItem.InventoryAssetAccountId, Debit = 0, Credit = cogsValueBase, Reference = $"Stock Out {freshItem.Name}" });
                    }

                    // 3. Update Order Line QtyShipped
                    var soLine = shipment.SalesOrder.Lines.First(l => l.Id == dbLine.SalesOrderLineId);
                    soLine.QtyShipped += inputLine.QtyShipped;

                    dbLine.QtyShipped = inputLine.QtyShipped;
                }

                // POST BATCH
                if (glLines.Any())
                {
                    // Enforce Period: Journal date must match shipment confirmation date
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(shipment.CompanyId, DateOnly.FromDateTime(DateTime.Today), "Shipment", $"Ship {shipment.ShipmentNumber}", glLines);
                    if (!string.IsNullOrEmpty(err)) throw new Exception($"Journal Creation Failed: {err}");

                    if (batchId.HasValue)
                    {
                        // THE FIX: Actually capture and handle GL Engine posting failures
                        var postErr = await _glOps.PostBatchAsync(shipment.CompanyId, batchId.Value);
                        if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Engine Rejected Posting: {postErr}");

                        shipment.ShipmentBatchId = batchId;
                    }
                }

                shipment.Status = ShipmentStatus.Shipped;
                shipment.ShippedDate = DateTime.UtcNow;
                shipment.ConfirmedBy = confirmedBy;

                bool allShipped = shipment.SalesOrder.Lines.Where(l => l.Item != null && !l.Item.IsService).All(l => l.QtyShipped >= l.Quantity);

                // Update Order Status cleanly
                if (shipment.SalesOrder.Status != OrderStatus.Invoiced && shipment.SalesOrder.Status != OrderStatus.PartiallyInvoiced)
                {
                    shipment.SalesOrder.Status = allShipped ? OrderStatus.Shipped : OrderStatus.PartiallyShipped;
                }

                if (isPartial)
                {
                    await CreateShipmentFromOrderAsync(shipment.SalesOrderId); // Spawns backorder
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