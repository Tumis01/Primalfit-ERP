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
        // 1. GET ORDERS
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

                // --- NEW: DISCOUNT MATH ---
                decimal discountValue = o.DiscountAmount;
                if (o.DiscountPercentage > 0)
                {
                    discountValue = subTotal * (o.DiscountPercentage / 100);
                }

                decimal discountedSubTotal = subTotal - discountValue;

                decimal taxPer = o.TaxId.HasValue && taxes.ContainsKey(o.TaxId.Value) ? taxes[o.TaxId.Value] : 0;
                decimal taxValue = discountedSubTotal * (taxPer / 100);

                // By assigning these two properties, IsFullyPaid will automatically evaluate to true if they match!
                o.GrandTotalForeign = discountedSubTotal + taxValue;
                o.AmountPaid = payments.ContainsKey(o.Id) ? payments[o.Id] : 0;
            }

            return orders;
        }

        // 2. GET SINGLE ORDER
        public async Task<SalesOrder?> GetOrderByIdAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order != null)
            {
                decimal subTotal = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

                // --- NEW: DISCOUNT MATH ---
                decimal discountValue = order.DiscountAmount;
                if (order.DiscountPercentage > 0)
                {
                    discountValue = subTotal * (order.DiscountPercentage / 100);
                }

                decimal discountedSubTotal = subTotal - discountValue;

                decimal taxPer = 0;
                if (order.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(order.TaxId.Value);
                    if (tax != null) taxPer = tax.Per;
                }

                decimal taxValue = discountedSubTotal * (taxPer / 100);

                // By assigning these two properties, IsFullyPaid will automatically evaluate to true if they match!
                order.GrandTotalForeign = discountedSubTotal + taxValue;

                order.AmountPaid = await ctx.PaymentApplications
                    .Where(pa => pa.InvoiceId == order.Id)
                    .SumAsync(pa => pa.AppliedAmount + pa.CashDiscountTaken);
            }

            return order;
        }

        // 3. CREATE / UPDATE ORDER
        public async Task<string> SaveOrderAsync(SalesOrder order)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (order.CompanyId == Guid.Empty) return "System Error: Company ID missing.";
            if (order.CustomerId == Guid.Empty) return "Customer is required.";
            if (!order.Lines.Any()) return "Order must have at least one line.";

            // Require Discount GL Account if a discount is applied
            if ((order.DiscountAmount > 0 || order.DiscountPercentage > 0) && order.DiscountGlAccountId == null)
            {
                return "You must select a Discount GL Account (Expense) to apply a discount.";
            }

            var lineItemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
            var itemsMap = await ctx.Items.Where(i => lineItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            bool hasPhysicalItems = order.Lines.Any(l => itemsMap.ContainsKey(l.ItemId) && !itemsMap[l.ItemId].IsService);

            if (order.Status != OrderStatus.Quote && hasPhysicalItems && order.WarehouseId == Guid.Empty)
            {
                return "Fulfillment Warehouse is required for physical items.";
            }

            if (order.Status != OrderStatus.Quote)
            {
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
            }

            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                order.Id = Guid.NewGuid();

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
                existing.TaxGLAccountId = order.TaxGLAccountId;

                // Save Discount fields
                existing.DiscountPercentage = order.DiscountPercentage;
                existing.DiscountAmount = order.DiscountAmount;
                existing.DiscountGlAccountId = order.DiscountGlAccountId;

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
                Status = OrderStatus.Order,
                CurrencyId = quote.CurrencyId,
                ExchangeRate = quote.ExchangeRate,
                WarehouseId = quote.WarehouseId,
                // Inherit Discounts
                DiscountPercentage = quote.DiscountPercentage,
                DiscountAmount = quote.DiscountAmount,
                DiscountGlAccountId = quote.DiscountGlAccountId
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

            var invoice = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = order.CompanyId,
                OrderNumber = $"INV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}",
                ConvertedFromQuoteNumber = order.OrderNumber,
                TaxId = order.TaxId,
                TaxGLAccountId = order.TaxGLAccountId,
                CustomerId = order.CustomerId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = OrderStatus.Draft,
                CurrencyId = order.CurrencyId,
                ExchangeRate = order.ExchangeRate,
                WarehouseId = order.WarehouseId,
                // Inherit Discounts
                DiscountPercentage = order.DiscountPercentage,
                DiscountAmount = order.DiscountAmount,
                DiscountGlAccountId = order.DiscountGlAccountId
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

        // --- UPDATED: POST INVOICE WITH DISCOUNT GL LOGIC ---
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
                if (order.Status == OrderStatus.Quote) return "Quotes cannot be invoiced directly.";

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

                // 1. CREDIT SALES REVENUE
                foreach (var line in order.Lines)
                {
                    if (line.Item == null || line.Quantity == 0) continue;

                    decimal lineTotalForeign = line.Quantity * line.UnitPrice;
                    decimal lineTotalBase = Math.Round(lineTotalForeign * rate, 2);

                    if (line.Item.SalesIncomeAccountId == Guid.Empty)
                        return $"Item '{line.Item.Name}' is missing a Sales Income GL Account mapping.";

                    // Credit Revenue for the FULL gross amount of the item
                    glLines.Add(new GLJournalLine { SegCoaId = line.Item.SalesIncomeAccountId, Debit = 0, Credit = lineTotalBase, Reference = $"Rev {line.Item.Name}" });
                    totalRevenueBase += lineTotalBase;
                }

                // 2. DEBIT DISCOUNT ALLOWED EXPENSE (Reduces the Net Revenue impact)
                decimal discountForeign = order.DiscountAmount;
                if (order.DiscountPercentage > 0)
                {
                    // Recalculate Foreign SubTotal to get exact percentage discount
                    decimal foreignSubTotal = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
                    discountForeign = foreignSubTotal * (order.DiscountPercentage / 100);
                }

                decimal discountBase = Math.Round(discountForeign * rate, 2);

                if (discountBase > 0)
                {
                    if (order.DiscountGlAccountId == null || order.DiscountGlAccountId == Guid.Empty)
                        return "A discount was applied, but no Discount GL Account was selected.";

                    glLines.Add(new GLJournalLine { SegCoaId = order.DiscountGlAccountId.Value, Debit = discountBase, Credit = 0, Reference = $"Discount {order.OrderNumber}" });
                }

                // 3. CREDIT TAX PAYABLE
                decimal discountedRevenueBase = totalRevenueBase - discountBase;
                decimal totalTaxBase = 0;

                if (order.TaxId.HasValue)
                {
                    var taxDef = await ctx.Taxes.FindAsync(order.TaxId);
                    if (taxDef != null && taxDef.Per > 0)
                    {
                        // Tax is calculated on the Post-Discount amount!
                        decimal taxAmountBase = discountedRevenueBase * (taxDef.Per / 100);
                        totalTaxBase = Math.Round(taxAmountBase, 2);

                        Guid targetGlId = order.TaxGLAccountId ?? taxDef.GLAccountId ?? Guid.Empty;
                        if (targetGlId == Guid.Empty) return $"Tax selected but no GL Account is mapped.";

                        glLines.Add(new GLJournalLine { SegCoaId = targetGlId, Debit = 0, Credit = totalTaxBase, Reference = $"{taxDef.TaxCode} on {order.OrderNumber}" });
                    }
                }

                // 4. DEBIT ACCOUNTS RECEIVABLE (The Net Amount the Customer actually owes)
                decimal grandTotalBase = discountedRevenueBase + totalTaxBase;

                if (order.Customer?.ReceivablesAccountId == null) return "Customer AR Account is missing.";

                glLines.Add(new GLJournalLine { SegCoaId = order.Customer.ReceivablesAccountId.Value, Debit = grandTotalBase, Credit = 0, Reference = $"Inv {order.OrderNumber}" });

                // POST BATCH
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