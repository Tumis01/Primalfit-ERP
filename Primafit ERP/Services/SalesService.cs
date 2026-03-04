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

        // 1. GET ORDERS (Now includes Payment Tracking logic!)
        public async Task<List<SalesOrder>> GetOrdersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var orders = await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId)
                .OrderByDescending(o => o.Date)
                .AsNoTracking()
                .ToListAsync();

            // Fetch Payments to determine if "IsFullyPaid"
            var invoiceIds = orders.Where(o => o.Status == OrderStatus.Invoiced).Select(o => o.Id).ToList();
            var payments = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

            foreach (var o in orders)
            {
                decimal subTotal = o.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal taxPer = o.TaxId.HasValue && taxes.ContainsKey(o.TaxId.Value) ? taxes[o.TaxId.Value] : 0;

                o.GrandTotalForeign = subTotal + (subTotal * taxPer / 100);
                o.AmountPaid = payments.ContainsKey(o.Id) ? payments[o.Id] : 0;
            }

            return orders;
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
        // 3. CREATE / UPDATE ORDER
        public async Task<string> SaveOrderAsync(SalesOrder order)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (order.CompanyId == Guid.Empty) return "System Error: Company ID missing.";
            if (order.CustomerId == Guid.Empty) return "Customer is required.";
            if (!order.Lines.Any()) return "Order must have at least one line.";

            var lineItemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
            var itemsMap = await ctx.Items.Where(i => lineItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            bool hasPhysicalItems = order.Lines.Any(l => itemsMap.ContainsKey(l.ItemId) && !itemsMap[l.ItemId].IsService);

            // 1. Force Warehouse selection for ALL states (Quote, Order, Invoice) if there are physical items
            if (hasPhysicalItems && order.WarehouseId == Guid.Empty)
            {
                return "Fulfillment Warehouse is required for physical items.";
            }

            // 2. Enforce Strict Inventory limits for ALL states (Quote, Order, Invoice)
            if (hasPhysicalItems)
            {
                foreach (var line in order.Lines)
                {
                    if (itemsMap.TryGetValue(line.ItemId, out var item) && !item.IsService)
                    {
                        decimal availableToPromise = await _invService.GetAvailableToPromiseAsync(
                            order.CompanyId, line.ItemId, order.WarehouseId, order.Id);

                        if (line.Quantity > availableToPromise)
                            return $"Cannot proceed: You requested {line.Quantity} of '{item.Name}', but only {availableToPromise} are available in the selected warehouse.";
                    }
                }
            }

            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                order.Id = Guid.NewGuid();

                // --- PREFIX LOGIC UPDATED FOR NEW ORDER TYPE ---
                string prefix = "INV";
                if (order.Status == OrderStatus.Quote) prefix = "QUO";
                else if (order.Status == OrderStatus.Order) prefix = "ORD";

                order.OrderNumber = $"{prefix}-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";

                foreach (var line in order.Lines)
                {
                    line.HeaderId = order.Id;
                    line.Id = Guid.NewGuid();
                    line.Header = null;
                }
                ctx.SalesOrders.Add(order);
            }
            else
            {
                var existing = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == order.Id);
                if (existing == null) return "Order not found.";
                if (existing.Status == OrderStatus.Invoiced) return "Cannot edit an order that has already been invoiced.";

                // --- PREVENT EDITING CONVERTED QUOTES/ORDERS ---
                if (existing.Status == OrderStatus.Quote || existing.Status == OrderStatus.Order)
                {
                    bool isConverted = await ctx.SalesOrders.AnyAsync(inv => inv.ConvertedFromQuoteNumber == existing.OrderNumber);
                    if (isConverted) return $"Cannot edit a {existing.Status} that has already been converted.";
                }

                existing.CustomerId = order.CustomerId;
                existing.WarehouseId = order.WarehouseId;
                existing.CurrencyId = order.CurrencyId;
                existing.ExchangeRate = order.ExchangeRate;
                existing.Date = order.Date;
                existing.TaxId = order.TaxId;
                existing.Status = order.Status;

                ctx.SalesOrderLines.RemoveRange(existing.Lines);
                foreach (var line in order.Lines)
                {
                    ctx.SalesOrderLines.Add(new SalesOrderLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = existing.Id,
                        ItemId = line.ItemId,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice
                    });
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> ConvertQuoteToOrderAsync(Guid quoteId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var quote = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == quoteId);

            if (quote == null) return "Quote not found.";
            if (quote.Status != OrderStatus.Quote) return "Only Quotes can be converted to Orders.";

            bool alreadyConverted = await ctx.SalesOrders.AnyAsync(o => o.ConvertedFromQuoteNumber == quote.OrderNumber);
            if (alreadyConverted) return "This quote has already been converted.";

            // Clone to a new Sales Order
            var order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = quote.CompanyId,
                OrderNumber = $"ORD-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}",
                ConvertedFromQuoteNumber = quote.OrderNumber,
                TaxId = quote.TaxId,
                TaxGLAccountId = quote.TaxGLAccountId,
                CustomerId = quote.CustomerId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = OrderStatus.Order, // SET TO ORDER
                CurrencyId = quote.CurrencyId,
                ExchangeRate = quote.ExchangeRate,
                WarehouseId = quote.WarehouseId
            };

            foreach (var line in quote.Lines)
            {
                order.Lines.Add(new SalesOrderLine { Id = Guid.NewGuid(), HeaderId = order.Id, ItemId = line.ItemId, Quantity = line.Quantity, UnitPrice = line.UnitPrice });
            }

            ctx.SalesOrders.Add(order);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<string> ConvertOrderToInvoiceAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status != OrderStatus.Order) return "Only Confirmed Orders can be converted to Invoices.";

            bool alreadyConverted = await ctx.SalesOrders.AnyAsync(o => o.ConvertedFromQuoteNumber == order.OrderNumber);
            if (alreadyConverted) return "This order has already been converted to an invoice.";

            // Clone to a new Invoice Draft
            var invoice = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = order.CompanyId,
                OrderNumber = $"INV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}",
                ConvertedFromQuoteNumber = order.OrderNumber, // Track lineage
                TaxId = order.TaxId,
                TaxGLAccountId = order.TaxGLAccountId,
                CustomerId = order.CustomerId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = OrderStatus.Draft, // SET TO DRAFT INVOICE
                CurrencyId = order.CurrencyId,
                ExchangeRate = order.ExchangeRate,
                WarehouseId = order.WarehouseId
            };

            foreach (var line in order.Lines)
            {
                invoice.Lines.Add(new SalesOrderLine { Id = Guid.NewGuid(), HeaderId = invoice.Id, ItemId = line.ItemId, Quantity = line.Quantity, UnitPrice = line.UnitPrice });
            }

            ctx.SalesOrders.Add(invoice);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

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
                if (line.Item == null || line.Item.IsService) continue;

                decimal currentStock = await _invService.GetStockLevel(line.ItemId, warehouseId);
                if (currentStock < line.Quantity)
                    return $"Fulfillment failed: Insufficient physical stock for {line.Item.Name}. Have: {currentStock}, Need: {line.Quantity}";

                ctx.StockLedgers.Add(new StockLedger
                {
                    Id = Guid.NewGuid(),
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = warehouseId,
                    QuantityChanged = -line.Quantity,
                    Type = StockMovementType.Sale,
                    CostAtTime = line.Item.WeightedAverageCost,
                    Reference = order.OrderNumber,
                    Date = DateTime.UtcNow
                });

                decimal cogsValueBase = line.Quantity * line.Item.WeightedAverageCost;
                if (cogsValueBase > 0)
                {
                    glLines.Add(new GLJournalLine { SegCoaId = line.Item.CostOfGoodsSoldAccountId, Debit = cogsValueBase, Credit = 0, Reference = $"COGS {line.Item.SKU}" });
                    glLines.Add(new GLJournalLine { SegCoaId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = cogsValueBase, Reference = $"Stock Out {line.Item.SKU}" });
                }
            }

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
                if (order.Status == OrderStatus.Quote) return "Quotes cannot be invoiced directly. Please convert it to an Invoice first.";

                var glLines = new List<GLJournalLine>();
                bool hasPhysicalItems = order.Lines.Any(l => l.Item != null && !l.Item.IsService);

                if (hasPhysicalItems)
                {
                    if (warehouseId == null || warehouseId == Guid.Empty)
                        return "Select a warehouse to fulfill physical items.";

                    string shipErr = await ShipOrderAsync(order.Id, warehouseId ?? Guid.Empty);
                    if (!string.IsNullOrEmpty(shipErr)) return shipErr;
                }

                decimal rate = order.ExchangeRate > 0 ? order.ExchangeRate : 1;
                decimal totalRevenueBase = 0;

                foreach (var line in order.Lines)
                {
                    if (line.Item == null || line.Quantity == 0) continue;

                    decimal lineTotalForeign = line.Quantity * line.UnitPrice;
                    decimal lineTotalBase = Math.Round(lineTotalForeign * rate, 2);

                    if (line.Item.SalesIncomeAccountId == Guid.Empty)
                        return $"Item '{line.Item.Name}' is missing a Sales Income GL Account mapping.";

                    glLines.Add(new GLJournalLine { SegCoaId = line.Item.SalesIncomeAccountId, Debit = 0, Credit = lineTotalBase, Reference = $"Rev {line.Item.Name}" });
                    totalRevenueBase += lineTotalBase;
                }

                decimal totalTaxBase = 0;
                if (order.TaxId.HasValue)
                {
                    var taxDef = await ctx.Taxes.FindAsync(order.TaxId);
                    if (taxDef != null && taxDef.Per > 0)
                    {
                        decimal taxAmountBase = totalRevenueBase * (taxDef.Per / 100);
                        totalTaxBase = Math.Round(taxAmountBase, 2);

                        Guid targetGlId = order.TaxGLAccountId ?? taxDef.GLAccountId ?? Guid.Empty;
                        if (targetGlId == Guid.Empty) return $"Tax selected but no GL Account is mapped.";

                        glLines.Add(new GLJournalLine { SegCoaId = targetGlId, Debit = 0, Credit = totalTaxBase, Reference = $"{taxDef.TaxCode} on {order.OrderNumber}" });
                    }
                }

                decimal grandTotalBase = totalRevenueBase + totalTaxBase;

                if (order.Customer?.ReceivablesAccountId == null) return "Customer AR Account is missing.";

                glLines.Add(new GLJournalLine { SegCoaId = order.Customer.ReceivablesAccountId.Value, Debit = grandTotalBase, Credit = 0, Reference = $"Inv {order.OrderNumber}" });

                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Sales Invoice", $"Inv {order.OrderNumber}", glLines);
                    if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                    if (batchId.HasValue) await _glOps.PostBatchAsync(order.CompanyId, batchId.Value);
                    order.InvoiceBatchId = batchId;
                }

                if (!order.OrderNumber.StartsWith("INV"))
                {
                    order.OrderNumber = $"INV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";
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

        public async Task<string> TerminateOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status == OrderStatus.Invoiced) return "Cannot terminate an order that has already been invoiced.";

            // --- NEW: PREVENT DELETING CONVERTED QUOTES ---
            if (order.Status == OrderStatus.Quote)
            {
                bool isConverted = await ctx.SalesOrders.AnyAsync(inv => inv.ConvertedFromQuoteNumber == order.OrderNumber);
                if (isConverted) return "Cannot delete a quote that has been converted. It must be kept for auditing.";
            }

            ctx.SalesOrderLines.RemoveRange(order.Lines);
            ctx.SalesOrders.Remove(order);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}