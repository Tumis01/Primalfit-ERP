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

        // 1. GET ORDERS (Company Scoped)
        public async Task<List<SalesOrder>> GetOrdersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Where(o => o.CompanyId == companyId)
                .OrderByDescending(o => o.Date)
                .AsNoTracking()
                .ToListAsync();
        }

        // 2. GET SINGLE ORDER (For Editing)
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

            // --- NEW ORDER ---
            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                // Generate secure ID and Number
                order.Id = Guid.NewGuid();
                // Simple auto-numbering logic (YYYY-MM-Random)
                order.OrderNumber = $"SO-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";

                // Ensure lines are linked
                foreach (var line in order.Lines) { line.HeaderId = order.Id; line.Id = Guid.NewGuid(); }

                ctx.SalesOrders.Add(order);
            }
            // --- UPDATE EXISTING ---
            else
            {
                var existing = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == order.Id);
                if (existing == null) return "Order not found.";
                if (existing.Status != OrderStatus.Draft) return "Cannot edit a confirmed order.";

                // Update Header
                existing.CustomerId = order.CustomerId;
                existing.CurrencyId = order.CurrencyId;
                existing.ExchangeRate = order.ExchangeRate;
                existing.Date = order.Date;

                // Update Lines (Simple approach: Remove all, Re-add all)
                ctx.SalesOrderLines.RemoveRange(existing.Lines);
                foreach (var line in order.Lines)
                {
                    line.HeaderId = existing.Id;
                    line.Id = Guid.NewGuid();
                    ctx.SalesOrderLines.Add(line);
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 4. SHIP ORDER (Physical Move)
        public async Task<string> ShipOrderAsync(Guid orderId, Guid warehouseId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status != OrderStatus.Draft && order.Status != OrderStatus.Confirmed) return "Invalid Order Status.";

            var glLines = new List<GLJournalLine>();

            foreach (var line in order.Lines)
            {
                if (line.Item == null) continue;

                // SKIPPING SERVICE ITEMS (Infinite Stock)
                if (line.Item.IsService) continue;

                // Checking Physical Stock
                decimal currentStock = await _invService.GetStockLevel(line.ItemId, warehouseId);
                if (currentStock < line.Quantity)
                    return $"Insufficient stock for {line.Item.Name}. Have: {currentStock}, Need: {line.Quantity}";

                // Deduct Stock
                ctx.StockLedgers.Add(new StockLedger
                {
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = warehouseId,
                    QuantityChanged = -line.Quantity,
                    Type = StockMovementType.Sale,
                    CostAtTime = line.Item.WeightedAverageCost,
                    Reference = order.OrderNumber
                });

                // COGS GL Entry
                decimal costVal = line.Quantity * line.Item.WeightedAverageCost;
                if (costVal > 0)
                {
                    glLines.Add(new GLJournalLine { AccountId = line.Item.CostOfGoodsSoldAccountId, Debit = costVal, Credit = 0, Reference = $"COGS {line.Item.SKU}" });
                    glLines.Add(new GLJournalLine { AccountId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = costVal, Reference = $"Stock Out {line.Item.SKU}" });
                }
            }

            // Post GL Batch
            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Shipment", $"Ship {order.OrderNumber}", glLines);
                if (!string.IsNullOrEmpty(err)) return err;
                order.ShipmentBatchId = batchId;
            }

            order.Status = OrderStatus.Shipped;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 5. INVOICE ORDER (Revenue)
        public async Task<string> InvoiceOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status != OrderStatus.Shipped) return "Order must be shipped before invoicing.";
            if (order.Customer?.ReceivablesAccountId == null) return "Customer AR Account missing.";

            decimal exRate = order.ExchangeRate > 0 ? order.ExchangeRate : 1;
            decimal totalLocalAmount = order.Lines.Sum(l => l.LineTotal) * exRate;

            var glLines = new List<GLJournalLine>();

            // Dr Accounts Receivable
            glLines.Add(new GLJournalLine { AccountId = order.Customer.ReceivablesAccountId.Value, Debit = totalLocalAmount, Credit = 0, Reference = $"Inv {order.OrderNumber}" });

            // Cr Sales Revenue (Per Line)
            foreach (var line in order.Lines)
            {
                decimal lineRevenue = line.LineTotal * exRate;
                glLines.Add(new GLJournalLine { AccountId = line.Item.SalesIncomeAccountId, Debit = 0, Credit = lineRevenue, Reference = $"Rev {line.Item.Name}" });
            }

            var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Invoice", $"Inv {order.OrderNumber}", glLines);
            if (!string.IsNullOrEmpty(err)) return err;

            order.Status = OrderStatus.Invoiced;
            order.InvoiceBatchId = batchId;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}