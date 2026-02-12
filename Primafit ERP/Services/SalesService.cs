using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class SalesService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly InventoryService _invService;

        public SalesService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps, InventoryService invService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
        }

        // 1. GET ORDERS
        public async Task<List<SalesOrder>> GetOrdersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId)
                .OrderByDescending(o => o.Date)
                .AsNoTracking()
                .ToListAsync();
        }

        // 2. GET SINGLE ORDER
        public async Task<SalesOrder?> GetOrderByIdAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == orderId);
        }

        // 3. CREATE / UPDATE ORDER
        public async Task<string> SaveOrderAsync(SalesOrder order)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (order.CompanyId == Guid.Empty) return "System Error: Company ID missing.";
            if (order.CustomerId == Guid.Empty) return "Customer is required.";
            if (!order.Lines.Any()) return "Order must have at least one line.";

            // --- FIX: SMART WAREHOUSE VALIDATION ---
            // Only require a warehouse if there are PHYSICAL items in the cart.
            // We need to fetch items to check their type.
            var lineItemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
            var itemsMap = await ctx.Items.Where(i => lineItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            bool hasPhysicalItems = order.Lines.Any(l => itemsMap.ContainsKey(l.ItemId) && !itemsMap[l.ItemId].IsService);

            if (hasPhysicalItems && order.WarehouseId == Guid.Empty)
            {
                return "Fulfillment Warehouse is required for physical items.";
            }

            // --- INVENTORY RESERVATION CHECK (Physical Only) ---
            foreach (var line in order.Lines)
            {
                if (itemsMap.TryGetValue(line.ItemId, out var item) && !item.IsService)
                {
                    decimal availableToPromise = await _invService.GetAvailableToPromiseAsync(
                        order.CompanyId, line.ItemId, order.WarehouseId, order.Id);

                    if (line.Quantity > availableToPromise)
                        return $"Cannot reserve {line.Quantity} of {item.Name}. Only {availableToPromise} available.";
                }
            }

            // --- NEW ORDER ---
            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                order.Id = Guid.NewGuid();
                order.OrderNumber = $"SO-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";
                order.Status = OrderStatus.Confirmed; // Auto-confirm

                foreach (var line in order.Lines)
                {
                    line.HeaderId = order.Id;
                    line.Id = Guid.NewGuid();
                    line.Header = null;
                }
                ctx.SalesOrders.Add(order);
            }
            // --- UPDATE EXISTING ---
            else
            {
                var existing = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == order.Id);
                if (existing == null) return "Order not found.";
                if (existing.Status == OrderStatus.Invoiced) return "Cannot edit an order that has already been invoiced.";

                existing.CustomerId = order.CustomerId;
                existing.WarehouseId = order.WarehouseId;
                existing.CurrencyId = order.CurrencyId;
                existing.ExchangeRate = order.ExchangeRate;
                existing.Date = order.Date;
                existing.TaxId = order.TaxId;

                ctx.SalesOrderLines.RemoveRange(existing.Lines);

                foreach (var line in order.Lines)
                {
                    var newLine = new SalesOrderLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = existing.Id,
                        ItemId = line.ItemId,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice
                    };
                    ctx.SalesOrderLines.Add(newLine);
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 4. SHIP ORDER (Physical Stock Deduction)
        public async Task<string> ShipOrderAsync(Guid orderId, Guid warehouseId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";

            var glLines = new List<GLJournalLine>();

            foreach (var line in order.Lines)
            {
                // FIX: Skip services completely for shipping/stock deduction
                if (line.Item == null || line.Item.IsService) continue;

                // 1. Check Physical Stock
                decimal currentStock = await _invService.GetStockLevel(line.ItemId, warehouseId);
                if (currentStock < line.Quantity)
                    return $"Fulfillment failed: Insufficient physical stock for {line.Item.Name}. Have: {currentStock}, Need: {line.Quantity}";

                // 2. Deduct Stock Ledger
                ctx.StockLedgers.Add(new StockLedger
                {
                    Id = Guid.NewGuid(),
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = warehouseId,
                    QuantityChanged = -line.Quantity, // Deduct
                    Type = StockMovementType.Sale,
                    CostAtTime = line.Item.WeightedAverageCost,
                    Reference = order.OrderNumber,
                    Date = DateTime.UtcNow
                });

                // 3. COGS Journal (Base Currency)
                decimal cogsValueBase = line.Quantity * line.Item.WeightedAverageCost;
                if (cogsValueBase > 0)
                {
                    glLines.Add(new GLJournalLine { AccountId = line.Item.CostOfGoodsSoldAccountId, Debit = cogsValueBase, Credit = 0, Reference = $"COGS {line.Item.SKU}" });
                    glLines.Add(new GLJournalLine { AccountId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = cogsValueBase, Reference = $"Stock Out {line.Item.SKU}" });
                }
            }

            // 4. Post Shipment Journal (Only if physical items existed)
            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Shipment", $"Ship {order.OrderNumber}", glLines);
                if (!string.IsNullOrEmpty(err)) return err;

                if (batchId.HasValue) await _glOps.PostBatchAsync(order.CompanyId, batchId.Value);
                order.ShipmentBatchId = batchId;
            }

            order.Status = OrderStatus.Shipped;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 5. INVOICE ORDER (Financial Posting + Auto Ship)
        public async Task<string> InvoiceOrderAsync(Guid orderId, Guid? warehouseId = null)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                var order = await ctx.SalesOrders
                    .Include(o => o.Customer)
                    .Include(o => o.Lines).ThenInclude(l => l.Item)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null) return "Order not found.";
                if (order.Status == OrderStatus.Invoiced) return "Order is already invoiced.";

                var glLines = new List<GLJournalLine>();

                // --- STEP 1: AUTO-SHIP (Physical Items Only) ---
                bool hasPhysicalItems = order.Lines.Any(l => l.Item != null && !l.Item.IsService);

                if (hasPhysicalItems)
                {
                    if (warehouseId == null || warehouseId == Guid.Empty)
                        return "Select a warehouse to fulfill physical items.";

                    string shipErr = await ShipOrderAsync(order.Id, warehouseId ?? Guid.Empty);
                    if (!string.IsNullOrEmpty(shipErr)) return shipErr;
                }

                // --- STEP 2: CALCULATE FINANCIALS (All Items) ---
                decimal rate = order.ExchangeRate > 0 ? order.ExchangeRate : 1;
                decimal totalCreditsBase = 0;

                // 1. Credit Sales Revenue
                foreach (var line in order.Lines)
                {
                    if (line.Item == null || line.Quantity == 0) continue;

                    decimal lineRevForeign = line.Quantity * line.UnitPrice;
                    decimal lineRevBase = Math.Round(lineRevForeign * rate, 2);

                    if (line.Item.SalesIncomeAccountId == Guid.Empty)
                        return $"Item '{line.Item.Name}' is missing a Sales Income GL Account mapping.";

                    glLines.Add(new GLJournalLine
                    {
                        AccountId = line.Item.SalesIncomeAccountId,
                        Debit = 0,
                        Credit = lineRevBase,
                        Reference = $"Rev {line.Item.Name}"
                    });

                    totalCreditsBase += lineRevBase;
                }

                // 2. Tax Logic (Placeholder for future implementation)
                /* if (order.TaxId.HasValue) { ... } 
                */

                // 3. Debit Accounts Receivable
                if (order.Customer?.ReceivablesAccountId == null)
                    return "Customer AR Account is missing. Please configure it in Master Data.";

                glLines.Add(new GLJournalLine
                {
                    AccountId = order.Customer.ReceivablesAccountId.Value,
                    Debit = totalCreditsBase,
                    Credit = 0,
                    Reference = $"Inv {order.OrderNumber}"
                });

                // --- STEP 4: POST GL BATCH ---
                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                        order.CompanyId,
                        order.Date,
                        "Sales Invoice",
                        $"Inv {order.OrderNumber}",
                        glLines);

                    if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                    if (batchId.HasValue) await _glOps.PostBatchAsync(order.CompanyId, batchId.Value);
                    order.InvoiceBatchId = batchId;
                }

                order.Status = OrderStatus.Invoiced;
                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Invoice Error: {ex.Message}";
            }
        }

        // 6. TERMINATE ORDER
        public async Task<string> TerminateOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status == OrderStatus.Invoiced) return "Cannot terminate an order that has already been invoiced.";

            ctx.SalesOrderLines.RemoveRange(order.Lines);
            ctx.SalesOrders.Remove(order);

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}