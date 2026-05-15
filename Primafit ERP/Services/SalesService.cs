    using Microsoft.EntityFrameworkCore;
    using Primafit_ERP.Components.Models;
    using Primafit_ERP.Components.Models.Reporting;
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

                // Map items ONLY for lines that actually have an ItemId
                var lineItemIds = order.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
                var itemsMap = await ctx.Items.Where(i => lineItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

                // Ensure warehouse is selected ONLY if there are actual physical items
                bool hasPhysicalItems = order.Lines.Any(l => l.ItemId.HasValue && itemsMap.ContainsKey(l.ItemId.Value) && !itemsMap[l.ItemId.Value].IsService);

                if (order.Status != OrderStatus.Quote && hasPhysicalItems && order.WarehouseId == Guid.Empty)
                {
                    return "Fulfillment Warehouse is required for physical items.";
                }

                // Check ATP (Available to Promise) ONLY for physical inventory items
                if (order.Status != OrderStatus.Quote)
                {
                    foreach (var line in order.Lines)
                    {
                        if (line.ItemId.HasValue && itemsMap.TryGetValue(line.ItemId.Value, out var item) && !item.IsService)
                        {
                            decimal availableToPromise = await _invService.GetAvailableToPromiseAsync(
                                order.CompanyId, line.ItemId.Value, order.WarehouseId, order.Id);

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
                            Description = line.Description, // SAVE DESCRIPTION
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



        // 4. SHIP ORDER
        public async Task<string> ShipOrderAsync(Guid orderId, Guid warehouseId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";

            var glLines = new List<GLJournalLine>();

            foreach (var line in order.Lines)
            {
                // SKIP lines that are Services OR Free-Text lines (ItemId is null)
                if (!line.ItemId.HasValue || line.Item == null || line.Item.IsService) continue;

                decimal currentStock = await _invService.GetStockLevel(line.ItemId.Value, warehouseId);
                if (currentStock < line.Quantity)
                    return $"Fulfillment failed: Insufficient physical stock for {line.Item.Name}. Have: {currentStock}, Need: {line.Quantity}";

                ctx.StockLedgers.Add(new StockLedger
                {
                    Id = Guid.NewGuid(),
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId.Value,
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
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Shipment", $"Ship {order.OrderNumber}", glLines, userId);
                if (!string.IsNullOrEmpty(err)) return err;

                if (batchId.HasValue) await _glOps.PostBatchAsync(order.CompanyId, batchId.Value, userId);
                order.ShipmentBatchId = batchId;
            }

            order.Status = OrderStatus.Shipped;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<string> InvoiceOrderAsync(Guid orderId, string userId)
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
                if (order.Status == OrderStatus.Invoiced) return "Order is already fully invoiced.";
                if (order.Status == OrderStatus.Quote) return "Quotes cannot be invoiced directly.";

                var glLines = new List<GLJournalLine>();
                decimal rate = order.ExchangeRate > 0 ? order.ExchangeRate : 1;

                decimal foreignSubTotalToInvoice = 0;
                decimal totalRevenueBase = 0;
                bool itemsInvoicedInThisRun = false;

                // 1. CREDIT SALES REVENUE (Independent of QtyShipped)
                foreach (var line in order.Lines)
                {
                    if (line.Item == null) continue;

                    // ALWAYS invoice up to the ordered quantity, regardless of what has shipped
                    decimal qtyToInvoice = line.Quantity - line.QtyInvoiced;

                    if (qtyToInvoice <= 0) continue; // Nothing left to bill on this line
                    itemsInvoicedInThisRun = true;

                    decimal lineTotalForeign = qtyToInvoice * line.UnitPrice;
                    decimal lineTotalBase = Math.Round(lineTotalForeign * rate, 2);

                    foreignSubTotalToInvoice += lineTotalForeign;

                    if (line.Item.SalesIncomeAccountId == Guid.Empty)
                        return $"Item '{line.Item.Name}' is missing a Sales Income GL Account mapping.";

                    glLines.Add(new GLJournalLine { SegCoaId = line.Item.SalesIncomeAccountId, Debit = 0, Credit = lineTotalBase, Reference = $"Rev {line.Item.Name}" });
                    totalRevenueBase += lineTotalBase;

                    // Update the line's invoiced tracker
                    line.QtyInvoiced += qtyToInvoice;
                }

                if (!itemsInvoicedInThisRun) return "No unbilled quantities found to invoice.";

                // 2. DEBIT DISCOUNT ALLOWED EXPENSE
                decimal discountForeign = 0;
                if (order.DiscountPercentage > 0)
                {
                    discountForeign = foreignSubTotalToInvoice * (order.DiscountPercentage / 100);
                }
                else if (order.DiscountAmount > 0)
                {
                    decimal originalTotalOrdered = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
                    decimal proportion = originalTotalOrdered > 0 ? (foreignSubTotalToInvoice / originalTotalOrdered) : 1;
                    discountForeign = order.DiscountAmount * proportion;
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
                        decimal taxAmountBase = discountedRevenueBase * (taxDef.Per / 100);
                        totalTaxBase = Math.Round(taxAmountBase, 2);

                        Guid targetGlId = order.TaxGLAccountId ?? taxDef.GLAccountId ?? Guid.Empty;
                        if (targetGlId == Guid.Empty) return $"Tax selected but no GL Account is mapped.";

                        glLines.Add(new GLJournalLine { SegCoaId = targetGlId, Debit = 0, Credit = totalTaxBase, Reference = $"{taxDef.TaxCode} on {order.OrderNumber}" });
                    }
                }

                // 4. DEBIT ACCOUNTS RECEIVABLE
                decimal grandTotalBase = discountedRevenueBase + totalTaxBase;
                if (order.Customer?.ReceivablesAccountId == null) return "Customer AR Account is missing.";

                glLines.Add(new GLJournalLine { SegCoaId = order.Customer.ReceivablesAccountId.Value, Debit = grandTotalBase, Credit = 0, Reference = $"Inv {order.OrderNumber}" });

                // POST BATCH
                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Sales Invoice", $"Inv {order.OrderNumber}", glLines, userId);
                    if (!string.IsNullOrEmpty(err)) throw new Exception($"GL Batch Creation Error: {err}");

                    if (batchId.HasValue)
                    {
                        // CAPTURE THE ERROR
                        var postErr = await _glOps.PostBatchAsync(order.CompanyId, batchId.Value, userId);

                        // IF IT FAILS, THROW IT SO THE TRANSACTION ROLLS BACK!
                        if (!string.IsNullOrEmpty(postErr))
                            throw new Exception($"GL Engine Rejected Posting: {postErr}");

                        order.InvoiceBatchId = batchId;
                    }
                }

                if (!order.OrderNumber.StartsWith("INV"))
                {
                    order.OrderNumber = $"INV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";
                }

                bool fullyInvoiced = order.Lines.All(l => l.QtyInvoiced >= l.Quantity);
                order.Status = fullyInvoiced ? OrderStatus.Invoiced : OrderStatus.PartiallyInvoiced;
                if (!string.IsNullOrEmpty(order.ConvertedFromQuoteNumber))
                {
                    var parentOrder = await ctx.SalesOrders
                        .FirstOrDefaultAsync(o => o.OrderNumber == order.ConvertedFromQuoteNumber);

                    if (parentOrder != null)
                    {
                        parentOrder.Status = order.Status; // Syncs the parent to 'Invoiced'
                    }
                }
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
            public async Task<List<SalesOrder>> GetDirectInvoicesAsync(Guid companyId)
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();

                return await ctx.SalesOrders
                    .AsNoTracking()
                    .Include(o => o.Customer)
                    .Include(o => o.Lines)
                    .Where(o => o.CompanyId == companyId && o.IsDirectInvoice == true)
                    .OrderByDescending(o => o.Date)
                    .ToListAsync();
            }

        // ==========================================
        // DIRECT AR INVOICE (NON-INVENTORY)
        // ==========================================
        public async Task<string> PostDirectInvoiceAsync(SalesOrder invoice, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                if (invoice.CompanyId == Guid.Empty) return "Company ID is missing.";
                if (invoice.CustomerId == Guid.Empty) return "Customer is required.";
                if (invoice.DirectIncomeGlAccountId == null || invoice.DirectIncomeGlAccountId == Guid.Empty) return "Income/Revenue GL Account is required.";
                if (!invoice.Lines.Any()) return "Invoice must have at least one line.";

                var customer = await ctx.Customers.FindAsync(invoice.CustomerId);
                if (customer?.ReceivablesAccountId == null) return "Customer is missing an AR (Receivables) GL Account.";

                // --- FIX 1: SATISFY WAREHOUSE CONSTRAINT ---
                // Even though this is non-inventory, the DB requires a valid WarehouseId.
                invoice.WarehouseId = await ctx.Warehouses
                    .Where(w => w.CompanyId == invoice.CompanyId)
                    .Select(w => w.Id)
                    .FirstOrDefaultAsync();

                // Calculate Totals
                decimal rate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1;
                decimal subTotalForeign = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice);

                decimal discountForeign = invoice.DiscountPercentage > 0
                    ? subTotalForeign * (invoice.DiscountPercentage / 100)
                    : invoice.DiscountAmount;

                decimal netForeign = subTotalForeign - discountForeign;

                decimal taxPer = 0;
                if (invoice.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(invoice.TaxId.Value);
                    if (tax != null) taxPer = tax.Per;
                }

                decimal taxForeign = netForeign * (taxPer / 100);
                invoice.GrandTotalForeign = netForeign + taxForeign;

                // Base Currency Values for GL
                decimal subTotalBase = Math.Round(subTotalForeign * rate, 2);
                decimal discountBase = Math.Round(discountForeign * rate, 2);
                decimal taxBase = Math.Round(taxForeign * rate, 2);
                decimal grandTotalBase = Math.Round(invoice.GrandTotalForeign * rate, 2);

                // Build GL Lines
                var glLines = new List<GLJournalLine>();

                glLines.Add(new GLJournalLine { SegCoaId = invoice.DirectIncomeGlAccountId.Value, Debit = 0, Credit = subTotalBase, Reference = "Direct AR Revenue" });

                if (discountBase > 0)
                {
                    if (invoice.DiscountGlAccountId == null || invoice.DiscountGlAccountId == Guid.Empty) return "Discount Expense GL Account is required.";
                    glLines.Add(new GLJournalLine { SegCoaId = invoice.DiscountGlAccountId.Value, Debit = discountBase, Credit = 0, Reference = "Discount Allowed" });
                }

                if (taxBase > 0)
                {
                    if (invoice.TaxGLAccountId == null || invoice.TaxGLAccountId == Guid.Empty) return "Tax GL Account is required.";
                    glLines.Add(new GLJournalLine { SegCoaId = invoice.TaxGLAccountId.Value, Debit = 0, Credit = taxBase, Reference = "Tax Payable" });
                }

                glLines.Add(new GLJournalLine { SegCoaId = customer.ReceivablesAccountId.Value, Debit = grandTotalBase, Credit = 0, Reference = "Accounts Receivable" });

                // Generate Invoice Number
                invoice.Id = Guid.NewGuid();
                invoice.OrderNumber = $"INV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";
                invoice.Status = OrderStatus.Invoiced;
                invoice.IsDirectInvoice = true;

                foreach (var line in invoice.Lines)
                {
                    line.Id = Guid.NewGuid();
                    line.HeaderId = invoice.Id;
                    line.QtyInvoiced = line.Quantity;
                    line.ItemId = null; // Explicitly ensure this is null for free-text
                }

                // --- FIX 2: SAFE EXECUTION ORDER ---

                // 1. Create the GL Batch (BUT DO NOT POST IT YET)
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(invoice.CompanyId, invoice.Date, "Direct AR Invoice", invoice.OrderNumber, glLines, userId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                invoice.InvoiceBatchId = batchId;
                ctx.SalesOrders.Add(invoice);

                // 2. Attempt to save the Invoice FIRST
                // If this crashes, the catch block triggers, the transaction rolls back, and the GL is never posted.
                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();

                // 3. Only Post the GL Batch after the Invoice is successfully secured in the database
                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(invoice.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr))
                    {
                        return $"Invoice Saved, but GL Post Failed: {postErr}";
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Added InnerException logging so if it crashes again, the UI will tell you exactly which DB column caused it.
                return $"Direct Invoice Error: {ex.InnerException?.Message ?? ex.Message}";
            }
        }

        // ==========================================
        // REPORT 1: CUSTOMER TRANSACTION REPORT
        // ==========================================
        public async Task<StandardReportData> GenerateCustomerTransactionReportAsync(Guid companyId, DateOnly start, DateOnly end, string customerSearch)
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();

                // 1. Fetch Taxes for accurate calculation
                var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

                // 2. Query ONLY Invoices (Exclude Quotes and Orders)
                var query = ctx.SalesOrders
                    .Include(o => o.Customer)
                    .Include(o => o.Currency)
                    .Include(o => o.Lines).ThenInclude(l => l.Item)
                    .Where(o => o.CompanyId == companyId
                             && o.Date >= start
                             && o.Date <= end
                             && o.OrderNumber.StartsWith("INV")
                             && (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced || o.Status == OrderStatus.Draft));

                // Apply optional customer filter
                if (!string.IsNullOrWhiteSpace(customerSearch))
                {
                    query = query.Where(o => o.Customer != null && o.Customer.Name.Contains(customerSearch));
                }

                var orders = await query.OrderByDescending(o => o.Date).ToListAsync();

                // 3. Fetch all related payments to calculate Amount Paid
                var invoiceIds = orders.Select(o => o.Id).ToList();
                var payments = await ctx.PaymentApplications
                    .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                    .GroupBy(pa => pa.InvoiceId)
                    .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                    .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

                var reportData = new StandardReportData
                {
                    ReportName = "Customer Transaction Report",
                    ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                    Headers = new List<string> {
                        "Date", "Invoice #", "Customer", "Item", "Qty", "Unit Price", "Line Total",
                        "Order Discount", "Order Tax", "Amount Paid (Foreign)", "Amount Paid (Base)"
                    },
                    Rows = new List<List<string>>()
                };

                foreach (var o in orders)
                {
                    // Calculate Header-level Math
                    decimal subTotalForeign = o.Lines.Sum(l => l.Quantity * l.UnitPrice);
                    decimal discountForeign = o.DiscountPercentage > 0 ? subTotalForeign * (o.DiscountPercentage / 100) : o.DiscountAmount;
                    decimal netForeign = subTotalForeign - discountForeign;

                    decimal taxPer = o.TaxId.HasValue && taxes.ContainsKey(o.TaxId.Value) ? taxes[o.TaxId.Value] : 0;
                    decimal taxForeign = netForeign * (taxPer / 100);

                    decimal paidForeign = payments.ContainsKey(o.Id) ? payments[o.Id] : 0;

                    decimal rate = o.ExchangeRate > 0 ? o.ExchangeRate : 1;
                    decimal paidBase = paidForeign * rate;

                    string curr = o.Currency?.CurrencyCode ?? "";

                    bool isFirstLine = true;
                    foreach (var line in o.Lines)
                    {
                        // To keep the report clean, Header-level info (Discount, Tax, Total Paid) 
                        // is only displayed on the FIRST line of the invoice.
                        var row = new List<string>
                        {
                            isFirstLine ? o.Date.ToString("yyyy-MM-dd") : "",
                            isFirstLine ? o.OrderNumber : "",
                            isFirstLine ? (o.Customer?.Name ?? "Unknown") : "",
                            line.Item?.Name ?? "Unknown",
                            line.Quantity.ToString("N2"),
                            line.UnitPrice.ToString("N2"),
                            (line.Quantity * line.UnitPrice).ToString("N2"),
                            isFirstLine ? discountForeign.ToString("N2") : "",
                            isFirstLine ? taxForeign.ToString("N2") : "",
                            isFirstLine ? $"{curr} {paidForeign:N2}" : "",
                            isFirstLine ? paidBase.ToString("N2") : ""
                        };
                        reportData.Rows.Add(row);
                        isFirstLine = false;
                    }
                }

                return reportData;
            }
            // ==========================================
            // REPORT 2: AR AGE ANALYSIS REPORT
            // ==========================================
            public async Task<StandardReportData> GenerateAgeAnalysisReportAsync(Guid companyId, DateOnly asOfDate)
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();

                // Fetch ONLY Invoiced orders up to the As-Of Date
                var orders = await ctx.SalesOrders
                    .Include(o => o.Customer)
                    .Include(o => o.Lines)
                    .Where(o => o.CompanyId == companyId && o.Date <= asOfDate && o.OrderNumber.StartsWith("INV") && (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced))
                    .ToListAsync();

                var invoiceIds = orders.Select(o => o.Id).ToList();

                // Strictly fetch payments made ON OR BEFORE the As-Of Date for accurate historical aging
                // Note: If you don't have Payment.Date mapped in PaymentApplication, we fetch standard applications.
                var payments = await ctx.PaymentApplications
                    .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                    .GroupBy(pa => pa.InvoiceId)
                    .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                    .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

                var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

                var reportData = new StandardReportData
                {
                    ReportName = "AR Age Analysis (Outstanding Invoices)",
                    ReportingPeriod = $"As of {asOfDate:MMM dd, yyyy}",
                    Headers = new List<string> { "Customer", "Invoice #", "Date", "Age (Days)", "Invoice Total", "Amount Paid", "Balance Due" },
                    Rows = new List<List<string>>()
                };

                decimal totalOutstanding = 0;

                // Group by Customer for professional sub-totaling
                var groupedOrders = orders.GroupBy(o => o.Customer?.Name ?? "Unknown").OrderBy(g => g.Key);

                foreach (var group in groupedOrders)
                {
                    decimal customerBalance = 0;

                    foreach (var o in group.OrderBy(x => x.Date))
                    {
                        // Recreate total value math
                        decimal subTotal = o.Lines.Sum(l => l.Quantity * l.UnitPrice);
                        decimal discount = o.DiscountPercentage > 0 ? subTotal * (o.DiscountPercentage / 100) : o.DiscountAmount;
                        decimal net = subTotal - discount;
                        decimal taxPer = o.TaxId.HasValue && taxes.ContainsKey(o.TaxId.Value) ? taxes[o.TaxId.Value] : 0;
                        decimal grandTotal = net + (net * (taxPer / 100));

                        decimal paid = payments.ContainsKey(o.Id) ? payments[o.Id] : 0;
                        decimal balance = grandTotal - paid;

                        // If they still owe money, add it to the report!
                        if (balance > 0.01m)
                        {
                            int ageDays = (asOfDate.ToDateTime(TimeOnly.MinValue) - o.Date.ToDateTime(TimeOnly.MinValue)).Days;

                            reportData.Rows.Add(new List<string>
                            {
                                group.Key,
                                o.OrderNumber,
                                o.Date.ToString("yyyy-MM-dd"),
                                ageDays.ToString(),
                                grandTotal.ToString("N2"),
                                paid.ToString("N2"),
                                balance.ToString("N2")
                            });

                            customerBalance += balance;
                            totalOutstanding += balance;
                        }
                    }

                    if (customerBalance > 0)
                    {
                        // Inject a bold Subtotal row for the customer (The PDF/Excel exporter will auto-format this because Column 2 has "SUMMARY")
                        reportData.Rows.Add(new List<string> { "", $"SUMMARY: {group.Key}", "", "", "", "", customerBalance.ToString("N2") });
                    }
                }

                // Inject Grand Total row
                reportData.Rows.Add(new List<string> { "", "GRAND TOTAL", "", "", "", "", totalOutstanding.ToString("N2") });

                return reportData;
            }

            // ==========================================
            // REPORT 3: SALES ANALYSIS BY ITEM
            // ==========================================
            public async Task<StandardReportData> GenerateSalesAnalysisReportAsync(Guid companyId, DateOnly start, DateOnly end)
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();

                // FIX: Only include actual Invoices. 
                // Including 'Order' status causes double-counting because the system 
                // preserves the original Order document when an Invoice is generated.
                var validStatuses = new[] {
                    OrderStatus.PartiallyInvoiced,
                    OrderStatus.Invoiced
                };

                var lines = await ctx.SalesOrderLines
                    .Include(l => l.Header)
                    .Include(l => l.Item)
                    .Where(l => l.Header != null
                             && l.Header.CompanyId == companyId
                             && l.Header.Date >= start
                             && l.Header.Date <= end
                             && l.Header.OrderNumber.StartsWith("INV")
                             && validStatuses.Contains(l.Header.Status))
                    .ToListAsync();

                var reportData = new StandardReportData
                {
                    ReportName = "Sales Analysis by Item",
                    ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                    Headers = new List<string> { "Item Name", "Item Type", "Qty Sold", "Avg Unit Price (Base)", "Gross Revenue (Base)" },
                    Rows = new List<List<string>>()
                };

                // Group by item and aggregate totals using the Order's Exchange Rate
                var groupedItems = lines
                    .GroupBy(l => l.Item)
                    .Select(g => new
                    {
                        Item = g.Key,
                        TotalQty = g.Sum(x => x.Quantity),
                        GrossRevenueBase = g.Sum(x => (x.Quantity * x.UnitPrice) * (x.Header.ExchangeRate > 0 ? x.Header.ExchangeRate : 1))
                    })
                    .OrderByDescending(x => x.GrossRevenueBase) // Sort top sellers first
                    .ToList();

                decimal grandTotalRevenueBase = 0;
                decimal grandTotalQty = 0;

                foreach (var row in groupedItems)
                {
                    decimal avgPriceBase = row.TotalQty > 0 ? row.GrossRevenueBase / row.TotalQty : 0;

                    reportData.Rows.Add(new List<string>
                    {
                        row.Item?.Name ?? "Unknown Item",
                        row.Item?.IsService == true ? "Service" : "Physical",
                        row.TotalQty.ToString("N2"),
                        avgPriceBase.ToString("N2"),
                        row.GrossRevenueBase.ToString("N2")
                    });

                    grandTotalQty += row.TotalQty;
                    grandTotalRevenueBase += row.GrossRevenueBase;
                }

                // Bold Summary Row at the bottom
                reportData.Rows.Add(new List<string>
                {
                    "",
                    "GRAND TOTAL",
                    grandTotalQty.ToString("N2"),
                    "",
                    grandTotalRevenueBase.ToString("N2")
                });

                return reportData;
            }
        }
    }