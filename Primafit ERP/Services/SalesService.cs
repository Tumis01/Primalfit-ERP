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
        private readonly TransactionMappingService _mappingService;

        public SalesService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps, InventoryService invService, TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService;
        }

        public async Task<List<SalesOrder>> GetOrdersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var orders = await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.CustomTransactionType)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId)
                .OrderByDescending(o => o.Date)
                .ToListAsync();

            if (!orders.Any()) return orders;

            var invoiceIds = orders
                .Where(o => o.OrderNumber.StartsWith("INV") || o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced || o.InvoiceBatchId != null)
                .Select(o => o.Id)
                .ToList();

            var paymentsMap = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            var creditNotesMap = await ctx.CreditNotes
                .Where(cn => invoiceIds.Contains(cn.SalesOrderId) && cn.Status == CreditNoteStatus.Posted)
                .GroupBy(cn => cn.SalesOrderId)
                .Select(g => new { InvoiceId = g.Key, TotalCredited = g.Sum(x => x.TotalAmount) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalCredited);

            var lineCreditsMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => invoiceIds.Contains(cnl.Header!.SalesOrderId) && cnl.Header.Status == CreditNoteStatus.Posted)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));
            var lineRefundsMap = await ctx.ReceiptRefundLines
                .Include(rrl => rrl.Header)
                .Where(rrl => invoiceIds.Contains(rrl.Header!.SalesOrderId) && rrl.Header.Status == ReceiptRefundStatus.Posted)
                .GroupBy(rrl => rrl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

            foreach (var o in orders)
            {
                foreach (var line in o.Lines)
                {
                    line.QtyCredited = lineCreditsMap.TryGetValue(line.Id, out var creditedQty) ? creditedQty : 0;
                    decimal returnedQty = lineRefundsMap.TryGetValue(line.Id, out var refQty) ? refQty : 0;
                    line.QtyReturned = returnedQty;
                }

                decimal subTotal = o.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal discountValue = o.DiscountPercentage > 0 ? subTotal * (o.DiscountPercentage / 100) : o.DiscountAmount;
                decimal discountedSubTotal = subTotal - discountValue;

                decimal taxPer = o.TaxId.HasValue && taxes.TryGetValue(o.TaxId.Value, out var p) ? p : 0;
                decimal taxValue = discountedSubTotal * (taxPer / 100);

                decimal rawGrandTotal = discountedSubTotal + taxValue;
                decimal totalCredited = creditNotesMap.TryGetValue(o.Id, out var creditedAmt) ? creditedAmt : 0;

                o.CreditNoteTotal = totalCredited;
                o.GrandTotalForeign = rawGrandTotal;
                o.AmountPaid = paymentsMap.TryGetValue(o.Id, out var paidAmt) ? paidAmt : 0;
            }

            return orders;
        }
        public async Task<SalesOrder?> GetOrderByIdAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.CustomTransactionType)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order != null)
            {
                // 1. Fetch line-level Credit Note reductions
                var lineCreditsMap = await ctx.CreditNoteLines
                    .Include(cnl => cnl.Header)
                    .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status == CreditNoteStatus.Posted)
                    .GroupBy(cnl => cnl.SalesOrderLineId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                // 2. Fetch line-level Receipt Refund returns
                var lineRefundsMap = await ctx.ReceiptRefundLines
                    .Include(rrl => rrl.Header)
                    .Where(rrl => rrl.Header!.SalesOrderId == orderId && rrl.Header.Status == ReceiptRefundStatus.Posted)
                    .GroupBy(rrl => rrl.SalesOrderLineId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                foreach (var line in order.Lines)
                {
                    line.QtyCredited = lineCreditsMap.TryGetValue(line.Id, out var cQty) ? cQty : 0;
                    line.QtyReturned = lineRefundsMap.TryGetValue(line.Id, out var rQty) ? rQty : 0;
                }

                // 3. Totals and Tax Computations
                decimal subTotal = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal discountValue = order.DiscountPercentage > 0 ? subTotal * (order.DiscountPercentage / 100) : order.DiscountAmount;
                decimal discountedSubTotal = subTotal - discountValue;

                decimal taxPer = 0;
                if (order.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(order.TaxId.Value);
                    if (tax != null) taxPer = tax.Per;
                }

                decimal taxValue = discountedSubTotal * (taxPer / 100);
                order.GrandTotalForeign = discountedSubTotal + taxValue;

                order.CreditNoteTotal = await ctx.CreditNotes
                    .Where(cn => cn.SalesOrderId == orderId && cn.Status == CreditNoteStatus.Posted)
                    .SumAsync(cn => cn.TotalAmount);

                order.AmountPaid = await ctx.PaymentApplications
                    .Where(pa => pa.InvoiceId == order.Id)
                    .SumAsync(pa => pa.AppliedAmount + pa.CashDiscountTaken);
            }

            return order;
        }

        public async Task<string> SaveOrderAsync(SalesOrder order)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (order.CompanyId == Guid.Empty) return "System Error: Company ID missing.";
            if (order.CustomerId == Guid.Empty) return "Customer is required.";
            if (!order.Lines.Any()) return "Order must have at least one line.";

            var lineItemIds = order.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
            var itemsMap = await ctx.Items.Where(i => lineItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            bool hasPhysicalItems = order.Lines.Any(l => l.ItemId.HasValue && itemsMap.ContainsKey(l.ItemId.Value) && !itemsMap[l.ItemId.Value].IsService);

            if (order.Status != OrderStatus.Quote && hasPhysicalItems && order.WarehouseId == Guid.Empty)
            {
                return "Fulfillment Warehouse is required for physical items.";
            }

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

            if (!string.IsNullOrWhiteSpace(order.OrderNumber))
            {
                bool orderNumberExists = await ctx.SalesOrders
                    .AnyAsync(o => o.CompanyId == order.CompanyId
                                && o.OrderNumber.ToLower() == order.OrderNumber.ToLower()
                                && o.Id != order.Id);

                if (orderNumberExists)
                    return $"Document Number '{order.OrderNumber}' already exists inside this company profile.";
            }

            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                if (order.Id == Guid.Empty) order.Id = Guid.NewGuid();

                if (string.IsNullOrWhiteSpace(order.OrderNumber))
                {
                    string prefix = "INV";
                    if (order.Status == OrderStatus.Quote) prefix = "QUO";
                    else if (order.Status == OrderStatus.Order) prefix = "ORD";

                    bool isDuplicate = true;
                    string generatedNumber = string.Empty;

                    while (isDuplicate)
                    {
                        generatedNumber = $"{prefix}-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                        isDuplicate = await ctx.SalesOrders.AnyAsync(o => o.CompanyId == order.CompanyId && o.OrderNumber == generatedNumber);
                    }
                    order.OrderNumber = generatedNumber;
                }

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
                if (existing.InvoiceBatchId.HasValue)
                {
                    var batch = await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == existing.InvoiceBatchId.Value);
                    if (batch != null && batch.Status == BatchStatus.Posted)
                        return "Cannot edit an invoice that has already been committed to the General Ledger.";
                }
                else if (existing.Status == OrderStatus.Invoiced)
                {
                    return "Cannot edit an invoice that has already been posted to the ledger.";
                }

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

                existing.DiscountPercentage = order.DiscountPercentage;
                existing.DiscountAmount = order.DiscountAmount;
                existing.DiscountGlAccountId = order.DiscountGlAccountId;
                existing.CustomTransactionTypeId = order.CustomTransactionTypeId;
                existing.ReceivablesGlAccountId = order.ReceivablesGlAccountId;
                existing.DirectIncomeGlAccountId = order.DirectIncomeGlAccountId;

                existing.Status = order.Status;

                ctx.SalesOrderLines.RemoveRange(existing.Lines);
                foreach (var line in order.Lines)
                {
                    ctx.SalesOrderLines.Add(new SalesOrderLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = existing.Id,
                        ItemId = line.ItemId,
                        Description = line.Description,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice
                    });
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // =========================================================
        // FIXED: CONVERT QUOTE TO ORDER (Collision Proofed)
        // =========================================================
        public async Task<string> ConvertQuoteToOrderAsync(Guid quoteId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var quote = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == quoteId);

            if (quote == null) return "Quote not found.";
            if (quote.Status != OrderStatus.Quote) return "Only Quotes can be converted to Orders.";

            bool alreadyConverted = await ctx.SalesOrders.AnyAsync(o => o.ConvertedFromQuoteNumber == quote.OrderNumber);
            if (alreadyConverted) return "This quote has already been converted.";

            // Unique Validation Checking Sequence
            bool isDuplicate = true;
            string generatedOrderNumber = string.Empty;

            while (isDuplicate)
            {
                generatedOrderNumber = $"ORD-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                isDuplicate = await ctx.SalesOrders.AnyAsync(o => o.CompanyId == quote.CompanyId && o.OrderNumber == generatedOrderNumber);
            }

            var order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = quote.CompanyId,
                OrderNumber = generatedOrderNumber,
                ConvertedFromQuoteNumber = quote.OrderNumber,
                TaxId = quote.TaxId,
                TaxGLAccountId = quote.TaxGLAccountId,
                CustomerId = quote.CustomerId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = OrderStatus.Order,
                CurrencyId = quote.CurrencyId,
                ExchangeRate = quote.ExchangeRate,
                WarehouseId = quote.WarehouseId,
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

        // =========================================================
        // FIXED: CONVERT ORDER TO INVOICE (Collision Proofed)
        // =========================================================
        public async Task<string> ConvertOrderToInvoiceAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status != OrderStatus.Order) return "Only Confirmed Orders can be converted to Invoices.";

            bool alreadyConverted = await ctx.SalesOrders.AnyAsync(o => o.ConvertedFromQuoteNumber == order.OrderNumber);
            if (alreadyConverted) return "This order has already been converted to an invoice.";

            // Unique Validation Checking Sequence
            bool isDuplicate = true;
            string generatedInvoiceNumber = string.Empty;

            while (isDuplicate)
            {
                generatedInvoiceNumber = $"INV-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                isDuplicate = await ctx.SalesOrders.AnyAsync(o => o.CompanyId == order.CompanyId && o.OrderNumber == generatedInvoiceNumber);
            }

            var invoice = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = order.CompanyId,
                OrderNumber = generatedInvoiceNumber,
                ConvertedFromQuoteNumber = order.OrderNumber,
                TaxId = order.TaxId,
                TaxGLAccountId = order.TaxGLAccountId,
                CustomerId = order.CustomerId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = OrderStatus.Draft,
                CurrencyId = order.CurrencyId,
                ExchangeRate = order.ExchangeRate,
                WarehouseId = order.WarehouseId,
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
            if (order.Status == OrderStatus.Shipped) return "Order has already been completely fulfilled.";

            // 1. Fetch all historically posted pre-shipment credit notes for this invoice
            var creditedQuantitiesMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status == CreditNoteStatus.Posted)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var glLines = new List<GLJournalLine>();
            bool physicalItemsProcessed = false;

            foreach (var line in order.Lines)
            {
                if (!line.ItemId.HasValue || line.Item == null || line.Item.IsService) continue;

                // 2. Compute the actual net quantity remaining to ship after balancing adjustments
                decimal alreadyCredited = creditedQuantitiesMap.TryGetValue(line.Id, out var creditedQty) ? creditedQty : 0;
                decimal netQtyToShip = line.Quantity - alreadyCredited;

                // If a credit note completely wiped this line item out, skip shipment mechanics entirely
                if (netQtyToShip <= 0)
                {
                    line.QtyShipped = 0;
                    continue;
                }

                physicalItemsProcessed = true;

                // 3. Validate actual warehouse stock availability against the balanced net target
                decimal currentStock = await _invService.GetStockLevel(line.ItemId.Value, warehouseId);
                if (currentStock < netQtyToShip)
                    return $"Fulfillment failed: Insufficient physical stock for {line.Item.Name}. Have: {currentStock}, Balanced Target Need: {netQtyToShip}";

                // 4. Log physical stock ledger depletion matching the balanced quantity
                ctx.StockLedgers.Add(new StockLedger
                {
                    Id = Guid.NewGuid(),
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId.Value,
                    WarehouseId = warehouseId,
                    QuantityChanged = -netQtyToShip,
                    Type = StockMovementType.Sale,
                    CostAtTime = line.Item.WeightedAverageCost,
                    Reference = order.OrderNumber,
                    Date = DateTime.UtcNow
                });

                // 5. Compute true Cost of Goods Sold (COGS) strictly on what is physically leaving
                decimal cogsValueBase = Math.Round(netQtyToShip * line.Item.WeightedAverageCost, 2);
                if (cogsValueBase > 0)
                {
                    glLines.Add(new GLJournalLine { SegCoaId = line.Item.CostOfGoodsSoldAccountId, Debit = cogsValueBase, Credit = 0, Reference = $"COGS {line.Item.SKU} (Net Shipped)" });
                    glLines.Add(new GLJournalLine { SegCoaId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = cogsValueBase, Reference = $"Stock Out {line.Item.SKU} (Net Shipped)" });
                }

                line.QtyShipped = netQtyToShip; // Save the exact net count dispatched to database records
            }

            // 6. Post balanced inventory revaluation components to the general ledger
            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Shipment Fulfillment", $"Ship {order.OrderNumber}", glLines, userId);
                if (!string.IsNullOrEmpty(err)) return err;

                if (batchId.HasValue) await _glOps.PostBatchAsync(order.CompanyId, batchId.Value, userId);
                order.ShipmentBatchId = batchId;
            }

            order.Status = OrderStatus.Shipped;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 5. INVOICE ORDER
        public async Task<string> InvoiceOrderAsync(Guid orderId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                var order = await ctx.SalesOrders
                    .Include(o => o.Customer)
                    .Include(o => o.Lines).ThenInclude(l => l.Item)
                    .Include(o => o.CustomTransactionType)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null) return "Order not found.";

                // Prevent modification if already committed to the General Ledger
                if (order.InvoiceBatchId.HasValue)
                {
                    var existingBatch = await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == order.InvoiceBatchId.Value);
                    if (existingBatch != null && existingBatch.Status == BatchStatus.Posted)
                        return "This invoice has already been posted to the General Ledger and cannot be modified.";
                }

                if (order.Status == OrderStatus.Quote) return "Quotes cannot be invoiced directly.";

                var glLines = new List<GLJournalLine>();
                decimal rate = order.ExchangeRate > 0 ? order.ExchangeRate : 1;

                decimal foreignSubTotalToInvoice = 0;
                decimal totalRevenueBase = 0;
                bool itemsInvoicedInThisRun = false;

                TransactionGlMapping? customMapping = null;
                if (order.CustomTransactionTypeId.HasValue && order.CustomTransactionTypeId.Value != Guid.Empty)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.CompanyId == order.CompanyId && m.CustomTransactionTypeId == order.CustomTransactionTypeId.Value);
                }

                foreach (var line in order.Lines)
                {
                    if (line.Item == null) continue;

                    decimal qtyToInvoice = line.Quantity - line.QtyInvoiced;
                    if (qtyToInvoice <= 0 && !order.InvoiceBatchId.HasValue) continue;
                    itemsInvoicedInThisRun = true;

                    decimal lineTotalForeign = line.Quantity * line.UnitPrice;
                    decimal lineTotalBase = Math.Round(lineTotalForeign * rate, 2);

                    foreignSubTotalToInvoice += lineTotalForeign;

                    Guid revenueAccount = order.DirectIncomeGlAccountId
                        ?? customMapping?.OverrideCreditGlAccountId
                        ?? line.Item.SalesIncomeAccountId;

                    if (revenueAccount == Guid.Empty)
                    {
                        revenueAccount = await _mappingService.GetMappedAccountAsync(
                            order.CompanyId,
                            SystemTransactionType.SalesInvoice,
                            isDebit: false,
                            defaultAccountId: line.Item.SalesIncomeAccountId);
                    }

                    if (revenueAccount == Guid.Empty)
                        return $"Item '{line.Item.Name}' is missing a Sales Income GL Account mapping.";

                    glLines.Add(new GLJournalLine { SegCoaId = revenueAccount, Debit = 0, Credit = lineTotalBase, Reference = $"Rev {line.Item.Name}" });
                    totalRevenueBase += lineTotalBase;

                    line.QtyInvoiced = line.Quantity;
                }

                if (!itemsInvoicedInThisRun) return "No unbilled quantities found to invoice.";

                // Discount Line
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
                    Guid discountAccount = order.DiscountGlAccountId ?? Guid.Empty;
                    if (discountAccount == Guid.Empty)
                    {
                        discountAccount = await _mappingService.GetMappedAccountAsync(
                            order.CompanyId,
                            SystemTransactionType.DiscountAllowed,
                            isDebit: true,
                            defaultAccountId: Guid.Empty);
                    }

                    if (discountAccount == Guid.Empty)
                        return "A discount was applied, but no Discount Allowed account is configured.";

                    glLines.Add(new GLJournalLine { SegCoaId = discountAccount, Debit = discountBase, Credit = 0, Reference = $"Discount {order.OrderNumber}" });
                }

                decimal discountedRevenueBase = totalRevenueBase - discountBase;
                decimal totalTaxBase = 0;

                // Tax Line
                if (order.TaxId.HasValue)
                {
                    var taxDef = await ctx.Taxes.FindAsync(order.TaxId);
                    if (taxDef != null && taxDef.Per > 0)
                    {
                        decimal taxAmountBase = discountedRevenueBase * (taxDef.Per / 100);
                        totalTaxBase = Math.Round(taxAmountBase, 2);

                        Guid targetGlId = order.TaxGLAccountId ?? taxDef.GLAccountId ?? Guid.Empty;
                        if (targetGlId == Guid.Empty) return "Tax selected but no GL Account is mapped.";

                        glLines.Add(new GLJournalLine { SegCoaId = targetGlId, Debit = 0, Credit = totalTaxBase, Reference = $"{taxDef.TaxCode} on {order.OrderNumber}" });
                    }
                }

                // Accounts Receivable (Debit)
                decimal grandTotalBase = discountedRevenueBase + totalTaxBase;

                Guid arAccount = order.ReceivablesGlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? order.Customer?.ReceivablesAccountId
                    ?? Guid.Empty;

                if (arAccount == Guid.Empty)
                {
                    arAccount = await _mappingService.GetMappedAccountAsync(
                        order.CompanyId,
                        SystemTransactionType.SalesInvoice,
                        isDebit: true,
                        defaultAccountId: order.Customer?.ReceivablesAccountId ?? Guid.Empty);
                }

                if (arAccount == Guid.Empty) return "Customer AR Account mapping is missing.";

                glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = grandTotalBase, Credit = 0, Reference = $"Inv {order.OrderNumber}" });

                if (!order.OrderNumber.StartsWith("INV"))
                {
                    bool isDuplicate = true;
                    string generatedInvoiceString = string.Empty;

                    while (isDuplicate)
                    {
                        generatedInvoiceString = $"INV-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                        isDuplicate = await ctx.SalesOrders.AnyAsync(o => o.CompanyId == order.CompanyId && o.OrderNumber == generatedInvoiceString);
                    }
                    order.OrderNumber = generatedInvoiceString;
                }

                // STAGE TO BATCH ONLY (DO NOT POST TO GLTRANSACTION DIRECTLY)
                var (err, batchId) = await _glOps.StageSubledgerBatchAsync(
                    companyId: order.CompanyId,
                    txnDate: order.Date,
                    batchName: order.OrderNumber,
                    description: $"Sales Invoice: {order.OrderNumber} - {order.Customer?.Name}",
                    sourceReference: order.OrderNumber,
                    lines: glLines,
                    userId: userId,
                    existingBatchId: order.InvoiceBatchId
                );

                if (!string.IsNullOrEmpty(err)) throw new Exception($"GL Staging Error: {err}");

                order.InvoiceBatchId = batchId;
                order.Status = OrderStatus.Invoiced;

                if (!string.IsNullOrEmpty(order.ConvertedFromQuoteNumber))
                {
                    var parentOrder = await ctx.SalesOrders
                        .FirstOrDefaultAsync(o => o.OrderNumber == order.ConvertedFromQuoteNumber && o.CompanyId == order.CompanyId);

                    if (parentOrder != null) parentOrder.Status = order.Status;
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

            var invoices = await ctx.SalesOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId && o.IsDirectInvoice == true)
                .OrderByDescending(o => o.Date)
                .ToListAsync();

            if (!invoices.Any()) return invoices;

            var invoiceIds = invoices.Select(i => i.Id).ToList();

            var paymentsMap = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            var creditNotesMap = await ctx.CreditNotes
                .Where(cn => invoiceIds.Contains(cn.SalesOrderId) && cn.Status == CreditNoteStatus.Posted)
                .GroupBy(cn => cn.SalesOrderId)
                .Select(g => new { InvoiceId = g.Key, TotalCredited = g.Sum(x => x.TotalAmount) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalCredited);

            var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

            foreach (var inv in invoices)
            {
                decimal subTotal = inv.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal discount = inv.DiscountPercentage > 0 ? subTotal * (inv.DiscountPercentage / 100) : inv.DiscountAmount;
                decimal net = subTotal - discount;
                decimal taxPer = inv.TaxId.HasValue && taxes.TryGetValue(inv.TaxId.Value, out var per) ? per : 0;
                decimal tax = net * (taxPer / 100);

                inv.GrandTotalForeign = net + tax;
                inv.CreditNoteTotal = creditNotesMap.GetValueOrDefault(inv.Id, 0);
                inv.AmountPaid = paymentsMap.GetValueOrDefault(inv.Id, 0);
            }

            return invoices;
        }

        // 6. POST DIRECT INVOICE

        public async Task<string> PostDirectInvoiceAsync(SalesOrder invoice, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                if (invoice.CompanyId == Guid.Empty) return "Company ID is missing.";
                if (invoice.CustomerId == Guid.Empty) return "Customer is required.";
                if (!invoice.Lines.Any()) return "Invoice must have at least one line item.";

                if (invoice.Lines.Any(l => l.Quantity <= 0))
                    return "Validation Error: All line items must have a quantity greater than 0.";

                if (invoice.Lines.Any(l => l.UnitPrice < 0))
                    return "Validation Error: Line unit price cannot be negative.";

                // 1. Resolve existing order from DB first to get the existing batch ID reliably
                var existingOrder = invoice.Id != Guid.Empty
                    ? await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == invoice.Id && o.CompanyId == invoice.CompanyId)
                    : null;

                Guid? batchToUpdate = invoice.InvoiceBatchId ?? existingOrder?.InvoiceBatchId;

                // Prevent modification if already committed to GL
                if (batchToUpdate.HasValue)
                {
                    var existingBatch = await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchToUpdate.Value);
                    if (existingBatch != null && existingBatch.Status == BatchStatus.Posted)
                        return "This direct invoice has already been committed to the General Ledger and cannot be modified.";
                }

                var customer = await ctx.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CustomerId);
                if (customer == null) return "Selected customer could not be resolved from master records.";

                invoice.WarehouseId = await ctx.Warehouses
                    .Where(w => w.CompanyId == invoice.CompanyId)
                    .Select(w => w.Id)
                    .FirstOrDefaultAsync();

                decimal rate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1;
                decimal subTotalForeign = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice);

                if (invoice.DiscountPercentage < 0 || invoice.DiscountPercentage > 100)
                    return "Validation Error: Discount percentage must be between 0 and 100.";

                decimal discountForeign = invoice.DiscountPercentage > 0
                    ? subTotalForeign * (invoice.DiscountPercentage / 100)
                    : invoice.DiscountAmount;

                if (discountForeign > subTotalForeign)
                    return "Validation Error: Discount amount cannot exceed subtotal gross value.";

                decimal netForeign = subTotalForeign - discountForeign;

                decimal taxPer = 0;
                if (invoice.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == invoice.TaxId.Value);
                    if (tax != null) taxPer = tax.Per;
                }

                decimal taxForeign = netForeign * (taxPer / 100);
                invoice.GrandTotalForeign = netForeign + taxForeign;

                decimal subTotalBase = Math.Round(subTotalForeign * rate, 2);
                decimal discountBase = Math.Round(discountForeign * rate, 2);
                decimal taxBase = Math.Round(taxForeign * rate, 2);
                decimal grandTotalBase = Math.Round(invoice.GrandTotalForeign * rate, 2);

                var glLines = new List<GLJournalLine>();

                TransactionGlMapping? customMapping = null;
                if (invoice.CustomTransactionTypeId.HasValue && invoice.CustomTransactionTypeId.Value != Guid.Empty)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.CompanyId == invoice.CompanyId && m.CustomTransactionTypeId == invoice.CustomTransactionTypeId.Value);
                }

                Guid directRevAccount = invoice.DirectIncomeGlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? await _mappingService.GetMappedAccountAsync(
                        invoice.CompanyId,
                        SystemTransactionType.DirectSalesInvoice,
                        isDebit: false,
                        defaultAccountId: Guid.Empty);

                if (directRevAccount == Guid.Empty)
                    return "Direct Invoice requires a Credit/Revenue Account. Please configure it in GL Settings or in the Route Modal.";

                glLines.Add(new GLJournalLine { SegCoaId = directRevAccount, Debit = 0, Credit = subTotalBase, Reference = "Direct AR Revenue" });

                if (discountBase > 0)
                {
                    Guid discountAccount = invoice.DiscountGlAccountId
                        ?? await _mappingService.GetMappedAccountAsync(
                            invoice.CompanyId,
                            SystemTransactionType.DiscountAllowed,
                            isDebit: true,
                            defaultAccountId: Guid.Empty);

                    if (discountAccount == Guid.Empty)
                        return "A discount was applied, but no Discount Allowed account is configured.";

                    glLines.Add(new GLJournalLine { SegCoaId = discountAccount, Debit = discountBase, Credit = 0, Reference = "Discount Allowed" });
                }

                if (taxBase > 0)
                {
                    Guid targetGlId = invoice.TaxGLAccountId ?? Guid.Empty;
                    if (targetGlId == Guid.Empty && invoice.TaxId.HasValue)
                    {
                        var taxObj = await ctx.Taxes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == invoice.TaxId.Value);
                        targetGlId = taxObj?.GLAccountId ?? Guid.Empty;
                    }

                    if (targetGlId == Guid.Empty) return "Tax is calculated but no Tax GL Account is mapped.";
                    glLines.Add(new GLJournalLine { SegCoaId = targetGlId, Debit = 0, Credit = taxBase, Reference = "Tax Payable" });
                }

                Guid arAccount = invoice.ReceivablesGlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? await _mappingService.GetMappedAccountAsync(
                        invoice.CompanyId,
                        SystemTransactionType.DirectSalesInvoice,
                        isDebit: true,
                        defaultAccountId: customer.ReceivablesAccountId ?? Guid.Empty);

                if (arAccount == Guid.Empty)
                    return "Customer is missing an AR (Receivables) Account mapping.";

                glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = grandTotalBase, Credit = 0, Reference = "Accounts Receivable" });

                if (string.IsNullOrWhiteSpace(invoice.OrderNumber) || !invoice.OrderNumber.StartsWith("INV"))
                {
                    bool isStringDuplicate = true;
                    string generatedDirectInvoiceNumber = string.Empty;

                    while (isStringDuplicate)
                    {
                        generatedDirectInvoiceNumber = $"INV-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                        isStringDuplicate = await ctx.SalesOrders.AnyAsync(o => o.CompanyId == invoice.CompanyId && o.OrderNumber == generatedDirectInvoiceNumber);
                    }
                    invoice.OrderNumber = generatedDirectInvoiceNumber;
                }

                // 2. Stage to batch using the verified batchToUpdate reference
                var (err, batchId) = await _glOps.StageSubledgerBatchAsync(
                    companyId: invoice.CompanyId,
                    txnDate: invoice.Date,
                    batchName: invoice.OrderNumber,
                    description: $"Direct AR Invoice: {invoice.OrderNumber} - {customer.Name}",
                    sourceReference: invoice.OrderNumber,
                    lines: glLines,
                    userId: userId,
                    existingBatchId: batchToUpdate
                );

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                invoice.InvoiceBatchId = batchId;
                invoice.Status = OrderStatus.Invoiced;
                invoice.IsDirectInvoice = true;

                invoice.Customer = null;
                invoice.Currency = null;
                invoice.CustomTransactionType = null;

                if (existingOrder != null)
                {
                    ctx.Entry(existingOrder).CurrentValues.SetValues(invoice);
                    ctx.SalesOrderLines.RemoveRange(existingOrder.Lines);

                    foreach (var line in invoice.Lines)
                    {
                        line.HeaderId = existingOrder.Id;
                        line.QtyInvoiced = line.Quantity;
                        line.ItemId = null;
                        line.Item = null;
                        line.Header = null;
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        ctx.SalesOrderLines.Add(line);
                    }
                }
                else
                {
                    if (invoice.Id == Guid.Empty) invoice.Id = Guid.NewGuid();

                    foreach (var line in invoice.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.HeaderId = invoice.Id;
                        line.QtyInvoiced = line.Quantity;
                        line.ItemId = null;
                        line.Item = null;
                        line.Header = null;
                    }

                    ctx.SalesOrders.Add(invoice);
                }

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Direct Invoice Error: {ex.InnerException?.Message ?? ex.Message}";
            }
        }

        // 7. REPORTS
        public async Task<StandardReportData> GenerateCustomerTransactionReportAsync(Guid companyId, DateOnly start, DateOnly end, string customerSearch)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

            var query = ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId
                         && o.Date >= start
                         && o.Date <= end
                         && o.OrderNumber.StartsWith("INV")
                         && (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced || o.Status == OrderStatus.Draft));

            if (!string.IsNullOrWhiteSpace(customerSearch))
            {
                query = query.Where(o => o.Customer != null && o.Customer.Name.Contains(customerSearch));
            }

            var orders = await query.OrderByDescending(o => o.Date).ToListAsync();
            var invoiceIds = orders.Select(o => o.Id).ToList();

            var payments = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            var creditNotesMap = await ctx.CreditNotes
                .Where(cn => invoiceIds.Contains(cn.SalesOrderId) && cn.Status == CreditNoteStatus.Posted)
                .GroupBy(cn => cn.SalesOrderId)
                .Select(g => new { InvoiceId = g.Key, TotalCredited = g.Sum(x => x.TotalAmount) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalCredited);

            var reportData = new StandardReportData
            {
                ReportName = "Customer Transaction Report",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                // UPDATED: Appended Credit Note and Net Total headers for audit visibility
                Headers = new List<string> {
            "Date", "Invoice #", "Customer", "Item", "Qty", "Unit Price", "Line Total",
            "Order Discount", "Order Tax", "Credit Applied", "Net Invoice Total", "Amount Paid (Foreign)", "Amount Paid (Base)"
        },
                Rows = new List<List<string>>()
            };

            foreach (var o in orders)
            {
                decimal subTotalForeign = o.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal discountForeign = o.DiscountPercentage > 0 ? subTotalForeign * (o.DiscountPercentage / 100) : o.DiscountAmount;
                decimal netForeign = subTotalForeign - discountForeign;

                decimal taxPer = o.TaxId.HasValue && taxes.ContainsKey(o.TaxId.Value) ? taxes[o.TaxId.Value] : 0;
                decimal taxForeign = netForeign * (taxPer / 100);

                decimal paidForeign = payments.TryGetValue(o.Id, out var pAmt) ? pAmt : 0;
                decimal rate = o.ExchangeRate > 0 ? o.ExchangeRate : 1;
                decimal paidBase = paidForeign * rate;

                // FIXED: Replaced undeclared discountedSubTotal with netForeign variable to fix compilation error
                decimal rawGrandTotal = netForeign + taxForeign;
                decimal creditedForeign = creditNotesMap.TryGetValue(o.Id, out var credAmt) ? credAmt : 0;
                decimal netGrandTotalForeign = rawGrandTotal - creditedForeign;

                string curr = o.Currency?.CurrencyCode ?? "";

                bool isFirstLine = true;
                foreach (var line in o.Lines)
                {
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
                // FIXED: Mapping the actual credit adjustments onto the data grid rows row-by-row
                isFirstLine ? (creditedForeign > 0 ? $"({creditedForeign.ToString("N2")})" : "0.00") : "",
                isFirstLine ? netGrandTotalForeign.ToString("N2") : "",
                isFirstLine ? $"{curr} {paidForeign:N2}" : "",
                isFirstLine ? paidBase.ToString("N2") : ""
            };
                    reportData.Rows.Add(row);
                    isFirstLine = false;
                }
            }

            return reportData;
        }
        public async Task<StandardReportData> GenerateAgeAnalysisReportAsync(Guid companyId, DateOnly asOfDate)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var orders = await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId && o.Date <= asOfDate && o.OrderNumber.StartsWith("INV") && (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced))
                .ToListAsync();

            var invoiceIds = orders.Select(o => o.Id).ToList();

            var payments = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            // FEATURE: Batch load credit notes to correctly calculate aging buckets
            var creditNotesMap = await ctx.CreditNotes
                .Where(cn => invoiceIds.Contains(cn.SalesOrderId) && cn.Status == CreditNoteStatus.Posted)
                .GroupBy(cn => cn.SalesOrderId)
                .Select(g => new { InvoiceId = g.Key, TotalCredited = g.Sum(x => x.TotalAmount) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalCredited);

            var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

            var reportData = new StandardReportData
            {
                ReportName = "AR Age Analysis (Outstanding Invoices)",
                ReportingPeriod = $"As of {asOfDate:MMM dd, yyyy}",
                Headers = new List<string> { "Customer", "Invoice #", "Date", "Age (Days)", "Invoice Total", "Amount Paid", "Balance Due" },
                Rows = new List<List<string>>()
            };

            decimal totalOutstanding = 0;
            var groupedOrders = orders.GroupBy(o => o.Customer?.Name ?? "Unknown").OrderBy(g => g.Key);

            foreach (var group in groupedOrders)
            {
                decimal customerBalance = 0;

                foreach (var o in group.OrderBy(x => x.Date))
                {
                    decimal subTotal = o.Lines.Sum(l => l.Quantity * l.UnitPrice);
                    decimal discount = o.DiscountPercentage > 0 ? subTotal * (o.DiscountPercentage / 100) : o.DiscountAmount;
                    decimal net = subTotal - discount;
                    decimal taxPer = o.TaxId.HasValue && taxes.ContainsKey(o.TaxId.Value) ? taxes[o.TaxId.Value] : 0;
                    decimal grandTotal = net + (net * (taxPer / 100));

                    decimal paid = payments.TryGetValue(o.Id, out var paidAmt) ? paidAmt : 0;
                    decimal credited = creditNotesMap.TryGetValue(o.Id, out var creditedAmt) ? creditedAmt : 0;
                    decimal balance = grandTotal - paid - credited;

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
                    reportData.Rows.Add(new List<string> { "", $"SUMMARY: {group.Key}", "", "", "", "", customerBalance.ToString("N2") });
                }
            }

            reportData.Rows.Add(new List<string> { "", "GRAND TOTAL", "", "", "", "", totalOutstanding.ToString("N2") });
            return reportData;
        }
        public async Task<StandardReportData> GenerateSalesAnalysisReportAsync(Guid companyId, DateOnly start, DateOnly end, string itemSearchQuery = "")
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var validStatuses = new[] { OrderStatus.PartiallyInvoiced, OrderStatus.Invoiced };

            // 1. Fetch sales lines matching parameters. 
            // CRITICAL FIX: Explicitly ignore direct invoices by filtering out entries without an Item ID
            var query = ctx.SalesOrderLines
                .Include(l => l.Header).ThenInclude(h => h.Customer)
                .Include(l => l.Item)
                .Where(l => l.Header != null
                         && l.ItemId != null
                         && l.Header.CompanyId == companyId
                         && l.Header.Date >= start
                         && l.Header.Date <= end
                         && l.Header.OrderNumber.StartsWith("INV")
                         && validStatuses.Contains(l.Header.Status));

            var lines = await query.ToListAsync();

            // 2. Filter solely by Item Name if a query string exists
            if (!string.IsNullOrWhiteSpace(itemSearchQuery))
            {
                string term = itemSearchQuery.Trim().ToLower();
                lines = lines.Where(l => l.Item?.Name != null && l.Item.Name.ToLower().Contains(term)).ToList();
            }

            // FEATURE: Extract target line IDs and build a look-up map for all matching posted credit note reductions
            var lineIds = lines.Select(l => l.Id).ToList();
            var lineCreditsMap = await ctx.CreditNoteLines
                .Include(cl => cl.Header)
                .Where(cl => lineIds.Contains(cl.SalesOrderLineId) && cl.Header!.Status == CreditNoteStatus.Posted)
                .GroupBy(cl => cl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var reportData = new StandardReportData
            {
                ReportName = "Sales Analysis Ledger Report",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string> { "Date / Reference", "Customer Name", "Type", "Quantity", "Price", "Amount" },
                Rows = new List<List<string>>()
            };

            // 3. Group and organize records strictly by item master names
            var groupedItems = lines
                .GroupBy(l => l.Item.Name)
                .OrderBy(g => g.Key)
                .ToList();

            decimal grandTotalRevenueBase = 0;
            decimal grandTotalQty = 0;

            foreach (var group in groupedItems)
            {
                string itemHeaderName = group.Key;
                var representativeLine = group.First();
                string itemType = representativeLine.Item.IsService ? "Service" : "Physical Goods";

                // FIXED: Compute true Net Quantity for the section header
                decimal itemGroupQty = group.Sum(x =>
                {
                    decimal creditedQty = lineCreditsMap.TryGetValue(x.Id, out var qty) ? qty : 0;
                    return x.Quantity - creditedQty;
                });

                // FIXED: Compute true Net Revenue Base Value for the section header
                decimal itemGroupRevenueBase = group.Sum(x =>
                {
                    decimal rate = x.Header.ExchangeRate > 0 ? x.Header.ExchangeRate : 1;
                    decimal creditedQty = lineCreditsMap.TryGetValue(x.Id, out var qty) ? qty : 0;
                    decimal netQty = x.Quantity - creditedQty;
                    return (netQty * x.UnitPrice) * rate;
                });

                // SECTION HEADER ROW (Reflects true balanced adjustments)
                reportData.Rows.Add(new List<string>
        {
            $"SECTION_HEADER:{itemHeaderName}",
            itemType,
            "",
            itemGroupQty.ToString("N2"),
            "",
            itemGroupRevenueBase.ToString("N2")
        });

                // TRANSACTION LINE DETAIL ROWS
                foreach (var line in group.OrderBy(l => l.Header.Date))
                {
                    decimal currentRate = line.Header.ExchangeRate > 0 ? line.Header.ExchangeRate : 1;

                    // FIXED: Isolate line-level reductions to modify line display variables dynamically
                    decimal creditedQty = lineCreditsMap.TryGetValue(line.Id, out var qty) ? qty : 0;
                    decimal netLineQty = line.Quantity - creditedQty;

                    decimal basePrice = line.UnitPrice * currentRate;
                    decimal baseAmount = (netLineQty * line.UnitPrice) * currentRate;

                    reportData.Rows.Add(new List<string>
            {
                line.Header.Date.ToString("yyyy-MM-dd") + " (" + line.Header.OrderNumber + ")",
                line.Header.Customer?.Name ?? "Unknown Customer",
                itemType,
                netLineQty.ToString("N2"), // Shows net quantity after credit note deductions
                basePrice.ToString("N2"),
                baseAmount.ToString("N2") // Shows net baseline amount after credit note deductions
            });
                }

                // SECTION FOOTER SPACER
                reportData.Rows.Add(new List<string> { "SECTION_SPACER", "", "", "", "", "" });

                grandTotalQty += itemGroupQty;
                grandTotalRevenueBase += itemGroupRevenueBase;
            }

            // FINAL GRAND TOTAL SUMMATION
            reportData.Rows.Add(new List<string>
    {
        "REPORT_TOTAL:GRAND TOTAL", "", "", grandTotalQty.ToString("N2"), "", grandTotalRevenueBase.ToString("N2")
    });

            return reportData;
        }
    }
}   