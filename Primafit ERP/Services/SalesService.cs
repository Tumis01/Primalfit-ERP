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
        private readonly IDocumentNumberingService _numberingService;
        private readonly DocumentWorkflowService _documentWorkflow;

        public SalesService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps, InventoryService invService, TransactionMappingService mappingService, IDocumentNumberingService numberingService, DocumentWorkflowService documentWorkflow)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService;
            _numberingService = numberingService;
            _documentWorkflow = documentWorkflow;
        }

        public async Task<List<SalesOrder>> GetOrdersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var orders = await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.CustomTransactionType)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Include(o => o.Lines).ThenInclude(l => l.SalesQuoteLine).ThenInclude(l => l!.SalesQuote)
                .Where(o => o.CompanyId == companyId)
                .OrderByDescending(o => o.Date)
                .ToListAsync();

            // Legacy invoice rows were copied to the dedicated SalesInvoices
            // table by the document-split migration. Keep the order register
            // from displaying both the old source row and its invoice record.
            var migratedInvoiceIds = (await ctx.SalesInvoices
                .Where(x => x.CompanyId == companyId)
                .Select(x => x.Id)
                .ToListAsync()).ToHashSet();
            orders = orders.Where(o => !o.IsInvoiceDocument || !migratedInvoiceIds.Contains(o.Id)).ToList();

            if (!orders.Any()) return orders;

            var invoiceIds = orders
                .Where(o => o.IsInvoiceDocument)
                .Select(o => o.Id)
                .ToList();

            var paymentsMap = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken + x.WithholdingAmountForeign) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            var creditNotesMap = await InvoiceFinancialBalances.SalesCreditsAsync(ctx, invoiceIds);
            var cashRefundsMap = await InvoiceFinancialBalances.SalesCashRefundsAsync(ctx, invoiceIds);
            foreach (var id in paymentsMap.Keys.ToList()) paymentsMap[id] = Math.Max(0, paymentsMap[id] - cashRefundsMap.GetValueOrDefault(id));

            var lineCreditsMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => (cnl.Header!.SalesOrderId.HasValue && invoiceIds.Contains(cnl.Header.SalesOrderId.Value)) && cnl.Header.Status == CreditNoteStatus.Posted)
                .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));
            var lineRefundsMap = await ctx.ReceiptRefundLines
                .Include(rrl => rrl.Header)
                .Where(rrl => (rrl.Header!.SalesOrderId.HasValue && invoiceIds.Contains(rrl.Header.SalesOrderId.Value)) && rrl.Header.Status == ReceiptRefundStatus.Posted)
                .Where(rrl => rrl.SalesOrderLineId.HasValue).GroupBy(rrl => rrl.SalesOrderLineId!.Value)
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
                    .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                // 2. Fetch line-level Receipt Refund returns
                var lineRefundsMap = await ctx.ReceiptRefundLines
                    .Include(rrl => rrl.Header)
                    .Where(rrl => rrl.Header!.SalesOrderId == orderId && rrl.Header.Status == ReceiptRefundStatus.Posted)
                    .Where(rrl => rrl.SalesOrderLineId.HasValue).GroupBy(rrl => rrl.SalesOrderLineId!.Value)
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
                    .SumAsync(pa => pa.AppliedAmount + pa.CashDiscountTaken + pa.WithholdingAmountForeign);
            }

            return order;
        }

        public async Task<string> SaveOrderAsync(SalesOrder order)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var sourceTransaction = order.Lines.Any(x => x.SalesQuoteLineId.HasValue)
                ? await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;

            if (order.CompanyId == Guid.Empty) return "System Error: Company ID missing.";
            if (order.CustomerId == Guid.Empty) return "Customer is required.";
            if (!order.Lines.Any()) return "Order must have at least one line.";
            if (order.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || (!l.ItemId.HasValue && string.IsNullOrWhiteSpace(l.Description)))) return "Each order line needs an item or description, positive quantity and non-negative price.";
            if (!await ctx.Customers.AnyAsync(c => c.Id == order.CustomerId && c.CompanyId == order.CompanyId)) return "Customer is not available in this company.";
            if (!await ctx.Currencies.AnyAsync(c => c.Id == order.CurrencyId && c.CompanyId == order.CompanyId)) return "Currency is not available in this company.";
            var orderCurrency = await ctx.SalesOrders.Where(x => x.Id == order.Id && x.CompanyId == order.CompanyId && x.CustomerId == order.CustomerId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            if (!orderCurrency.HasValue && !order.Lines.Any(x => x.SalesQuoteLineId.HasValue)) orderCurrency = await ctx.Customers.Where(x => x.Id == order.CustomerId && x.CompanyId == order.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            var orderCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(ctx, order.CompanyId, order.CurrencyId, order.ExchangeRate, orderCurrency);
            if (orderCurrencyError != null) return orderCurrencyError;

            if (order.WithholdingTaxId.HasValue)
            {
                var withholding = await ctx.WithholdingTaxes.AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Id == order.WithholdingTaxId.Value && w.CompanyId == order.CompanyId);
                if (withholding == null) return "Selected withholding tax is not configured for this company.";
                if (!withholding.SalesGlAccountId.HasValue || withholding.SalesGlAccountId == Guid.Empty)
                    return "Selected withholding tax has no sales receivable account mapped.";
                if (!await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == withholding.SalesGlAccountId.Value && a.CompanyId == order.CompanyId && a.IsActive))
                    return "Selected withholding tax sales receivable account is invalid or inactive.";
                order.WithholdingTaxName = withholding.Name;
                order.WithholdingPercentage = Math.Round(withholding.PercentageValue, 4);
                order.WithholdingSalesGlAccountId = withholding.SalesGlAccountId;
            }
            else
            {
                order.WithholdingTaxName = null;
                order.WithholdingPercentage = 0;
                order.WithholdingSalesGlAccountId = null;
            }

            var lineItemIds = order.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
            var itemsMap = await ctx.Items.Include(x => x.UomConversions).Where(i => lineItemIds.Contains(i.Id) && i.CompanyId == order.CompanyId).ToDictionaryAsync(i => i.Id);
            if (itemsMap.Count != lineItemIds.Count) return "An order item is unavailable in this company.";
            var conversionMap = await ctx.UomConversionRules.AsNoTracking()
                .Where(x => x.CompanyId == order.CompanyId && x.IsActive)
                .ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var companyUomIds = await ctx.UnitOfMeasures.AsNoTracking()
                .Where(x => x.CompanyId == order.CompanyId).Select(x => x.Id).ToHashSetAsync();
            var savedUoms = await ctx.SalesOrderLines.AsNoTracking().Where(x => x.HeaderId == order.Id && x.Header!.CompanyId == order.CompanyId).ToDictionaryAsync(x => x.Id);
            var quoteIds = order.Lines.Where(x => x.SalesQuoteLineId.HasValue).Select(x => x.SalesQuoteLineId!.Value).ToList();
            var quoteUoms = await ctx.SalesQuoteLines.AsNoTracking().Where(x => quoteIds.Contains(x.Id) && x.SalesQuote!.CompanyId == order.CompanyId).ToDictionaryAsync(x => x.Id);
            foreach (var line in order.Lines.Where(x => x.ItemId.HasValue && itemsMap.ContainsKey(x.ItemId.Value)))
            {
                var item = itemsMap[line.ItemId!.Value];
                if (!line.UomId.HasValue) line.UomId = item.UomId;
                if (line.UomId.HasValue && !companyUomIds.Contains(line.UomId.Value)) return "Selected line UOM is invalid.";
                if (savedUoms.TryGetValue(line.Id, out var savedUnit) && savedUnit.ItemId == line.ItemId && savedUnit.UomId == line.UomId && savedUnit.UomConversionFactor == line.UomConversionFactor && line.UomConversionFactor > 0) continue;
                if (line.SalesQuoteLineId.HasValue && quoteUoms.TryGetValue(line.SalesQuoteLineId.Value, out var quoteUnit) && quoteUnit.ItemId == line.ItemId && quoteUnit.UomId == line.UomId && quoteUnit.UomConversionFactor == line.UomConversionFactor && line.UomConversionFactor > 0) continue;
                if (line.UomId.HasValue && line.UomId != item.UomId
                    && !UomConversion.IsConfigured(item, line.UomId))
                    return $"The selected UOM for '{item.Name}' is not configured on this item.";
                var expected = UomConversion.FactorFor(item, line.UomId, conversionMap);
                if (line.UomConversionFactor <= 0 || Math.Abs(line.UomConversionFactor - expected) > 0.000000000000001m)
                    line.UomConversionFactor = expected;
            }

            if (order.WarehouseId != Guid.Empty && !await ctx.Warehouses.AnyAsync(w =>
                w.Id == order.WarehouseId && w.CompanyId == order.CompanyId &&
                (w.Type == WarehouseType.Both || w.Type == WarehouseType.Sales)))
                return "Select a warehouse available to the sales module.";

            if (!string.IsNullOrWhiteSpace(order.OrderNumber))
            {
                bool orderNumberExists = await ctx.SalesOrders
                    .AnyAsync(o => o.CompanyId == order.CompanyId
                                && o.OrderNumber.ToLower() == order.OrderNumber.ToLower()
                                && o.Id != order.Id);

                if (orderNumberExists)
                    return $"Document Number '{order.OrderNumber}' already exists inside this company profile.";
            }

            var dimensionError = await DimensionService.ValidateAsync(ctx, order.CompanyId, order.Lines.Select(x => x.DimensionSetId), order.Status != OrderStatus.Draft);
            if (dimensionError.Length > 0) return dimensionError;
            var sourceError = await OrderSourceValidation.PrepareSalesAsync(ctx, order);
            if (sourceError != null) return sourceError;

            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                if (order.Id == Guid.Empty) order.Id = Guid.NewGuid();

                if (string.IsNullOrWhiteSpace(order.OrderNumber))
                {
                    var target = order.Status switch
                    {
                        OrderStatus.Quote => SequenceTargetType.AR_Quote,
                        OrderStatus.Order => SequenceTargetType.AR_Order,
                        _ => SequenceTargetType.AR_Invoice
                    };
                    order.OrderNumber = await _numberingService.GenerateAndReserveNextNumberAsync(order.CompanyId, target, order.Date.ToDateTime(TimeOnly.MinValue));
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
                if (existing.Lines.Any(x => x.QtyShipped > 0)) return "An order with shipped lines is read-only.";
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

                if (existing.CompanyId != order.CompanyId) return "Cannot change an order's company.";
                if (order.Lines.Where(l => l.Id != Guid.Empty).GroupBy(l => l.Id).Any(g => g.Count() > 1)) return "An order line cannot be repeated.";
                if (existing.Lines.Any(l => l.SalesQuoteLineId.HasValue) && (existing.CustomerId != order.CustomerId || existing.CurrencyId != order.CurrencyId))
                    return "Customer and currency cannot be changed on an order created from quote lines.";
                var existingLineIds = existing.Lines.Select(l => l.Id).ToList();
                var shippedLineIds = await ctx.SalesShipmentLines.Where(l => l.SalesOrderLineId.HasValue && existingLineIds.Contains(l.SalesOrderLineId.Value)).Select(l => l.SalesOrderLineId!.Value).Distinct().ToListAsync();
                foreach (var sourceLine in existing.Lines.Where(l => l.SalesQuoteLineId.HasValue || shippedLineIds.Contains(l.Id)))
                {
                    var edited = order.Lines.SingleOrDefault(l => l.Id == sourceLine.Id);
                    if (edited == null || edited.SalesQuoteLineId != sourceLine.SalesQuoteLineId)
                        return "A sourced or shipped order line cannot be removed or have its source changed.";
                    if (shippedLineIds.Contains(sourceLine.Id) && (edited.ItemId != sourceLine.ItemId || edited.Quantity != sourceLine.Quantity))
                        return "A shipped order line cannot have its item or quantity changed.";
                }
                existing.CustomerId = order.CustomerId;
                existing.ProjectId = order.ProjectId;
                existing.WarehouseId = order.WarehouseId;
                existing.CurrencyId = order.CurrencyId;
                existing.ExchangeRate = order.ExchangeRate;
                existing.Date = order.Date;
                existing.TransactionDateTime = order.TransactionDateTime;
                existing.TaxId = order.TaxId;
                existing.TaxGLAccountId = order.TaxGLAccountId;
                existing.WithholdingTaxId = order.WithholdingTaxId;
                existing.WithholdingTaxName = order.WithholdingTaxName;
                existing.WithholdingPercentage = order.WithholdingPercentage;
                existing.WithholdingSalesGlAccountId = order.WithholdingSalesGlAccountId;

                existing.DiscountPercentage = order.DiscountPercentage;
                existing.DiscountAmount = order.DiscountAmount;
                existing.DiscountGlAccountId = order.DiscountGlAccountId;
                existing.CustomTransactionTypeId = order.CustomTransactionTypeId;
                existing.ReceivablesGlAccountId = order.ReceivablesGlAccountId;
                existing.DirectIncomeGlAccountId = order.DirectIncomeGlAccountId;

                existing.Status = order.Status;

                var existingLines = existing.Lines.ToDictionary(l => l.Id);
                var incomingIds = order.Lines.Select(l => l.Id).ToHashSet();
                ctx.SalesOrderLines.RemoveRange(existing.Lines.Where(l => !incomingIds.Contains(l.Id)));
                foreach (var line in order.Lines)
                {
                    if (existingLines.TryGetValue(line.Id, out var persisted))
                    {
                        persisted.ItemId = line.ItemId;
                        persisted.SalesQuoteLineId = line.SalesQuoteLineId;
                        persisted.Description = line.Description;
                        persisted.Quantity = line.Quantity;
                        persisted.UnitPrice = line.UnitPrice;
                        persisted.UomId = line.UomId;
                        persisted.UomName = line.UomName;
                        persisted.DimensionSetId = line.DimensionSetId; persisted.UomConversionFactor = line.UomConversionFactor;
                    }
                    else
                    {
                        ctx.SalesOrderLines.Add(new SalesOrderLine { DimensionSetId = line.DimensionSetId,  Id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id,
                            HeaderId = existing.Id, SalesQuoteLineId = line.SalesQuoteLineId, ItemId = line.ItemId, Description = line.Description, Quantity = line.Quantity,
                            UnitPrice = line.UnitPrice, UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor });
                    }
                }
            }

            try
            {
                await ctx.SaveChangesAsync();
                if (sourceTransaction != null) await sourceTransaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                return "The order could not be saved. A selected quote line may already have been used; refresh and try again.";
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                return "The order could not be saved because the database was busy or unavailable. Refresh and try again.";
            }
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

            var generatedOrderNumber = await _numberingService.GenerateAndReserveNextNumberAsync(quote.CompanyId, SequenceTargetType.AR_Order);

            var order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = quote.CompanyId,
                OrderNumber = generatedOrderNumber,
                ConvertedFromQuoteNumber = quote.OrderNumber,
                TaxId = quote.TaxId,
                TaxGLAccountId = quote.TaxGLAccountId,
                CustomerId = quote.CustomerId,
                ProjectId = quote.ProjectId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                TransactionDateTime = DateTime.Now,
                Status = OrderStatus.Order,
                CurrencyId = quote.CurrencyId,
                ExchangeRate = quote.ExchangeRate,
                WarehouseId = quote.WarehouseId,
                DiscountPercentage = quote.DiscountPercentage,
                DiscountAmount = quote.DiscountAmount,
                DiscountGlAccountId = quote.DiscountGlAccountId,
                WithholdingTaxId = quote.WithholdingTaxId,
                WithholdingTaxName = quote.WithholdingTaxName,
                WithholdingPercentage = quote.WithholdingPercentage,
                WithholdingSalesGlAccountId = quote.WithholdingSalesGlAccountId
            };

            foreach (var line in quote.Lines)
            {
                order.Lines.Add(new SalesOrderLine
                { DimensionSetId = line.DimensionSetId,
                    Id = Guid.NewGuid(), HeaderId = order.Id, ItemId = line.ItemId,
                    Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                    UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor
                });
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

            var generatedInvoiceNumber = await _numberingService.GenerateAndReserveNextNumberAsync(order.CompanyId, SequenceTargetType.AR_Invoice);

            var invoice = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = order.CompanyId,
                OrderNumber = generatedInvoiceNumber,
                ConvertedFromQuoteNumber = order.OrderNumber,
                TaxId = order.TaxId,
                TaxGLAccountId = order.TaxGLAccountId,
                CustomerId = order.CustomerId,
                ProjectId = order.ProjectId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                TransactionDateTime = DateTime.Now,
                Status = OrderStatus.Draft,
                CurrencyId = order.CurrencyId,
                ExchangeRate = order.ExchangeRate,
                WarehouseId = order.WarehouseId,
                DiscountPercentage = order.DiscountPercentage,
                DiscountAmount = order.DiscountAmount,
                DiscountGlAccountId = order.DiscountGlAccountId,
                WithholdingTaxId = order.WithholdingTaxId,
                WithholdingTaxName = order.WithholdingTaxName,
                WithholdingPercentage = order.WithholdingPercentage,
                WithholdingSalesGlAccountId = order.WithholdingSalesGlAccountId
            };

            foreach (var line in order.Lines)
            {
                invoice.Lines.Add(new SalesOrderLine
                { DimensionSetId = line.DimensionSetId,
                    Id = Guid.NewGuid(), HeaderId = invoice.Id, ItemId = line.ItemId,
                    Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                    UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor
                });
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
            if (!await ctx.Warehouses.AnyAsync(w => w.Id == warehouseId && w.CompanyId == order.CompanyId &&
                (w.Type == WarehouseType.Both || w.Type == WarehouseType.Sales)))
                return "Select a warehouse available to the sales module.";

            // 1. Fetch all historically posted pre-shipment credit notes for this invoice
            var creditedQuantitiesMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status == CreditNoteStatus.Posted)
                .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var glLines = new List<GLJournalLine>();
            var stockMovements = new List<StockLedger>();
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
                var stockMovement = new StockLedger
                { DimensionSetId = line.DimensionSetId,
                    Id = Guid.NewGuid(),
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId.Value,
                    WarehouseId = warehouseId,
                    QuantityChanged = -netQtyToShip,
                    Type = StockMovementType.Sale,
                    ProjectId = order.ProjectId,
                    CostAtTime = line.Item.WeightedAverageCost,
                    Reference = order.OrderNumber,
                    Date = DateTime.UtcNow
                };
                ctx.StockLedgers.Add(stockMovement);
                stockMovements.Add(stockMovement);

                // 5. Compute true Cost of Goods Sold (COGS) strictly on what is physically leaving
                decimal cogsValueBase = Math.Round(netQtyToShip * line.Item.WeightedAverageCost, 2);
                if (cogsValueBase > 0)
                {
                    glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = line.Item.CostOfGoodsSoldAccountId, Debit = cogsValueBase, Credit = 0, Reference = $"COGS {line.Item.Code} (Net Shipped)" });
                    glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = cogsValueBase, Reference = $"Stock Out {line.Item.Code} (Net Shipped)" });
                }

                line.QtyShipped = netQtyToShip; // Save the exact net count dispatched to database records
            }

            // 6. Post balanced inventory revaluation components to the general ledger
            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Shipment Fulfillment", $"Ship {order.OrderNumber}", glLines, userId, inheritedProjectId: order.ProjectId);
                if (!string.IsNullOrEmpty(err)) return err;
                if (stockMovements.Count > 0 && !batchId.HasValue)
                    return "GL staging did not create a review batch for this shipment.";
                if (batchId.HasValue)
                {
                    var postError = await _glOps.PostBatchAsync(order.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postError)) return postError;
                    foreach (var stockMovement in stockMovements)
                        stockMovement.GLBatchId = batchId;
                }
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

                    glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = revenueAccount, Debit = 0, Credit = lineTotalBase, Reference = $"Rev {line.Item.Name}" });
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

                if (string.IsNullOrWhiteSpace(order.OrderNumber))
                {
                    order.OrderNumber = await _numberingService.GenerateAndReserveNextNumberAsync(order.CompanyId, SequenceTargetType.AR_Invoice, order.Date.ToDateTime(TimeOnly.MinValue));
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
                    existingBatchId: order.InvoiceBatchId,
                    inheritedProjectId: order.ProjectId
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

            var documentInvoices = await ctx.SalesInvoices
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId && o.IsDirectInvoice)
                .OrderByDescending(o => o.Date)
                .ToListAsync();

            var invoices = documentInvoices.Select(o => new SalesOrder
            {
                Id = o.Id,
                CompanyId = o.CompanyId,
                CustomerId = o.CustomerId,
                Customer = o.Customer,
                CurrencyId = o.CurrencyId,
                Currency = o.Currency,
                WarehouseId = o.WarehouseId ?? Guid.Empty,
                ProjectId = o.ProjectId,
                OrderNumber = o.InvoiceNumber,
                Date = o.Date,
                TransactionDateTime = o.TransactionDateTime,
                Status = o.Status switch
                {
                    SalesInvoiceStatus.Posted => OrderStatus.Invoiced,
                    SalesInvoiceStatus.Cancelled => OrderStatus.Cancelled,
                    _ => OrderStatus.Draft
                },
                IsDirectInvoice = true,
                ExchangeRate = o.ExchangeRate,
                TaxId = o.TaxId,
                TaxGLAccountId = o.TaxGLAccountId,
                WithholdingTaxId = o.WithholdingTaxId,
                WithholdingTaxName = o.WithholdingTaxName,
                WithholdingPercentage = o.WithholdingPercentage,
                WithholdingSalesGlAccountId = o.WithholdingSalesGlAccountId,
                DiscountPercentage = o.DiscountPercentage,
                DiscountAmount = o.DiscountAmount,
                DiscountGlAccountId = o.DiscountGlAccountId,
                CustomTransactionTypeId = o.CustomTransactionTypeId,
                ReceivablesGlAccountId = o.ReceivablesGlAccountId,
                DirectIncomeGlAccountId = o.DirectIncomeGlAccountId,
                InvoiceBatchId = o.InvoiceBatchId ?? o.GLBatchId,
                Lines = o.Lines.Select(l => new SalesOrderLine
                { DimensionSetId = l.DimensionSetId,
                    Id = l.Id,
                    HeaderId = o.Id,
                    Description = l.Description,
                    Quantity = l.QuantityInvoiced,
                    UnitPrice = l.UnitPrice,
                    QtyInvoiced = l.QuantityInvoiced
                }).ToList(),
                GrandTotalForeign = o.TotalAmountForeign
            }).ToList();

            if (!invoices.Any()) return invoices;

            var invoiceIds = invoices.Select(i => i.Id).ToList();

            var paymentsMap = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken + x.WithholdingAmountForeign) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            var creditNotesMap = await InvoiceFinancialBalances.SalesCreditsAsync(ctx, invoiceIds);
            var cashRefundsMap = await InvoiceFinancialBalances.SalesCashRefundsAsync(ctx, invoiceIds);
            foreach (var id in paymentsMap.Keys.ToList()) paymentsMap[id] = Math.Max(0, paymentsMap[id] - cashRefundsMap.GetValueOrDefault(id));

            var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

            foreach (var inv in invoices)
            {
                decimal subTotal = inv.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal discount = inv.DiscountPercentage > 0 ? subTotal * (inv.DiscountPercentage / 100) : inv.DiscountAmount;
                decimal net = subTotal - discount;
                decimal taxPer = inv.TaxId.HasValue && taxes.TryGetValue(inv.TaxId.Value, out var per) ? per : 0;
                decimal tax = net * (taxPer / 100);

                if (inv.GrandTotalForeign <= 0) inv.GrandTotalForeign = net + tax;
                inv.CreditNoteTotal = creditNotesMap.GetValueOrDefault(inv.Id, 0);
                inv.AmountPaid = paymentsMap.GetValueOrDefault(inv.Id, 0);
            }

            return invoices;
        }

        // 6. POST DIRECT INVOICE

        public async Task<string> PostDirectInvoiceAsync(SalesOrder invoice, string userId)
        {
            return await SaveDirectInvoiceDraftAsync(invoice, userId);
        }

        public async Task<string> SaveDirectInvoiceDraftAsync(SalesOrder draft, string? submitUserId = null)
        {
            if (draft.CompanyId == Guid.Empty) return "Company is required.";
            if (draft.CustomerId == Guid.Empty) return "Customer is required.";
            if (draft.CurrencyId == Guid.Empty) return "Currency is required.";
            if (draft.Lines.Count == 0) return "Add at least one invoice line.";
            if (draft.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitPrice < 0))
                return "Every direct invoice line needs a description, a positive quantity and a non-negative price.";
            if (draft.DiscountPercentage < 0 || draft.DiscountPercentage > 100 || draft.DiscountAmount < 0)
                return "Discount values are invalid.";

            await using var db = await _dbFactory.CreateDbContextAsync();
            if (!await db.Customers.AnyAsync(x => x.Id == draft.CustomerId && x.CompanyId == draft.CompanyId))
                return "The selected customer is not in this company.";
            if (!await db.Currencies.AnyAsync(x => x.Id == draft.CurrencyId && x.CompanyId == draft.CompanyId))
                return "The selected currency is not in this company.";
            var routeError = await InvoiceRoutePolicy.ValidateAsync(db, draft.CompanyId, TransactionTemplateModule.AR, new InvoiceRoute {
                TemplateId = draft.CustomTransactionTypeId, DebitAccountId = draft.ReceivablesGlAccountId, CreditAccountId = draft.DirectIncomeGlAccountId, TaxAccountId = draft.TaxGLAccountId, DiscountAccountId = draft.DiscountGlAccountId });
            if (routeError != null) return routeError;
            var directCurrency = await db.SalesInvoices.Where(x => x.Id == draft.Id && x.CompanyId == draft.CompanyId && x.CustomerId == draft.CustomerId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync()
                ?? await db.Customers.Where(x => x.Id == draft.CustomerId && x.CompanyId == draft.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            var directCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(db, draft.CompanyId, draft.CurrencyId, draft.ExchangeRate, directCurrency);
            if (directCurrencyError != null) return directCurrencyError;
            if (draft.WithholdingTaxId.HasValue)
            {
                var withholding = await db.WithholdingTaxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == draft.WithholdingTaxId && x.CompanyId == draft.CompanyId);
                if (withholding?.SalesGlAccountId == null || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == withholding.SalesGlAccountId && x.CompanyId == draft.CompanyId && x.IsActive))
                    return "The selected withholding tax needs an active sales receivable account.";
                draft.WithholdingTaxName = withholding.Name; draft.WithholdingPercentage = withholding.PercentageValue;
                draft.WithholdingSalesGlAccountId = withholding.SalesGlAccountId;
            }
            else { draft.WithholdingTaxName = null; draft.WithholdingPercentage = 0; draft.WithholdingSalesGlAccountId = null; }

            if (draft.Id == Guid.Empty) draft.Id = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(draft.OrderNumber))
                draft.OrderNumber = await _numberingService.GenerateAndReserveNextNumberAsync(
                    draft.CompanyId, SequenceTargetType.AR_Invoice, draft.Date.ToDateTime(TimeOnly.MinValue));

            var duplicateNumber = await db.SalesInvoices.AnyAsync(x => x.CompanyId == draft.CompanyId
                && x.InvoiceNumber == draft.OrderNumber && x.Id != draft.Id);
            if (duplicateNumber) return $"Sales invoice number '{draft.OrderNumber}' is already in use.";

            await using var tx = await db.Database.BeginTransactionAsync();
            var existing = await db.SalesInvoices
                .FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {draft.Id} AND [CompanyId] = {draft.CompanyId}")
                .Include(x => x.Lines).FirstOrDefaultAsync();
            if (existing?.Status == SalesInvoiceStatus.Posted)
                return "A posted sales invoice cannot be edited.";
            if (existing?.GLBatchId is Guid batchId)
            {
                var batch = await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId && x.CompanyId == draft.CompanyId);
                if (batch?.Status == BatchStatus.Posted) return "A sales invoice committed to the General Ledger cannot be edited.";
            }

            if (existing?.GLBatchId.HasValue == true && submitUserId == null) return "Use Update Staged Invoice to save this invoice and refresh its review batch.";
            var invoice = existing ?? new SalesInvoice { Id = draft.Id, CompanyId = draft.CompanyId };
            invoice.CustomerId = draft.CustomerId;
            invoice.WarehouseId = draft.WarehouseId != Guid.Empty ? draft.WarehouseId : invoice.WarehouseId;
            invoice.ProjectId = draft.ProjectId;
            invoice.InvoiceNumber = draft.OrderNumber;
            invoice.Date = draft.Date;
            invoice.TransactionDateTime = draft.TransactionDateTime;
            invoice.Status = existing?.Status == SalesInvoiceStatus.InReview ? SalesInvoiceStatus.InReview : SalesInvoiceStatus.Draft;
            invoice.IsDirectInvoice = true;
            invoice.CurrencyId = draft.CurrencyId;
            invoice.ExchangeRate = draft.ExchangeRate > 0 ? draft.ExchangeRate : 1m;
            invoice.TaxId = draft.TaxId;
            invoice.TaxGLAccountId = draft.TaxGLAccountId;
            invoice.DiscountPercentage = draft.DiscountPercentage;
            invoice.DiscountAmount = draft.DiscountAmount;
            invoice.DiscountGlAccountId = draft.DiscountGlAccountId;
            invoice.WithholdingTaxId = draft.WithholdingTaxId;
            invoice.WithholdingTaxName = draft.WithholdingTaxName;
            invoice.WithholdingPercentage = draft.WithholdingPercentage;
            invoice.WithholdingSalesGlAccountId = draft.WithholdingSalesGlAccountId;
            invoice.ReceivablesGlAccountId = draft.ReceivablesGlAccountId;
            invoice.DirectIncomeGlAccountId = draft.DirectIncomeGlAccountId;
            invoice.CustomTransactionTypeId = draft.CustomTransactionTypeId;
            invoice.GLBatchId = existing?.GLBatchId ?? draft.InvoiceBatchId;
            invoice.InvoiceBatchId = existing?.InvoiceBatchId ?? draft.InvoiceBatchId;

            if (existing != null) { db.SalesInvoiceLines.RemoveRange(existing.Lines); existing.Lines.Clear(); }
            invoice.Lines = draft.Lines.Select(line => new SalesInvoiceLine
            { DimensionSetId = line.DimensionSetId,
                Id = Guid.NewGuid(),
                SalesInvoiceId = invoice.Id,
                ItemId = null,
                Description = line.Description?.Trim(),
                QuantityInvoiced = line.Quantity,
                UnitPrice = line.UnitPrice,
                RevenueGlAccountId = draft.DirectIncomeGlAccountId,
                TaxId = draft.TaxId,
                TaxGLAccountId = draft.TaxGLAccountId
            }).ToList();
            var gross = invoice.Lines.Sum(x => x.Amount);
            var discount = invoice.DiscountPercentage > 0 ? gross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, gross);
            var taxRate = invoice.TaxId.HasValue
                ? (await db.Taxes.FirstOrDefaultAsync(x => x.Id == invoice.TaxId && x.CompanyId == invoice.CompanyId))?.Per ?? 0m
                : 0m;
            invoice.TotalAmountForeign = gross - discount + (gross - discount) * taxRate / 100m;
            invoice.TotalAmount = Math.Round(invoice.TotalAmountForeign * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);

            if (existing == null) db.SalesInvoices.Add(invoice);
            else db.SalesInvoiceLines.AddRange(invoice.Lines);
            await db.SaveChangesAsync();
            if (submitUserId != null) {
                var error = await _documentWorkflow.StageSalesInvoiceAsync(db, invoice.Id, submitUserId);
                if (!string.IsNullOrEmpty(error)) return error;
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
            draft.Id = invoice.Id;
            draft.InvoiceBatchId = invoice.InvoiceBatchId ?? invoice.GLBatchId;
            return string.Empty;
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
                         && !ctx.SalesInvoices.Any(documentInvoice => documentInvoice.CompanyId == companyId && documentInvoice.Id == o.Id)
                         && o.Date >= start
                         && o.Date <= end
                         && (o.IsDirectInvoice || o.InvoiceBatchId != null
                             || (o.ConvertedFromQuoteNumber != null && o.Status == OrderStatus.Draft)
                             || o.OrderNumber.StartsWith("INV"))
                         && (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced || o.Status == OrderStatus.Draft));

            if (!string.IsNullOrWhiteSpace(customerSearch))
            {
                query = query.Where(o => o.Customer != null && o.Customer.Name.Contains(customerSearch));
            }

            var orders = await query.OrderByDescending(o => o.Date).ToListAsync();
            var documentInvoices = await ctx.SalesInvoices.AsNoTracking()
                .Include(x => x.Customer).Include(x => x.Currency)
                .Include(x => x.Lines).ThenInclude(x => x.Item)
                .Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end
                    && (string.IsNullOrWhiteSpace(customerSearch) || (x.Customer != null && x.Customer.Name.Contains(customerSearch)))
                    && (x.Status == SalesInvoiceStatus.Posted || x.Status == SalesInvoiceStatus.InReview)
                    && ((!x.GLBatchId.HasValue && x.Status == SalesInvoiceStatus.Posted) || (x.GLBatchId.HasValue
                        && ctx.GLBatches.Any(batch => batch.Id == x.GLBatchId.Value && batch.CompanyId == companyId && batch.Status == BatchStatus.Posted))))
                .ToListAsync();
            orders.AddRange(documentInvoices.Select(ToSalesOrderReportRecord));
            var invoiceIds = orders.Select(o => o.Id).ToList();

            var payments = await (from pa in ctx.PaymentApplications join payment in ctx.CustomerPayments on pa.CustomerPaymentId equals payment.Id
                where invoiceIds.Contains(pa.InvoiceId) && payment.CompanyId == companyId && payment.Status == PaymentStatus.Posted
                    && payment.Date <= end.ToDateTime(TimeOnly.MaxValue)
                    && (!payment.GLBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == payment.GLBatchId && b.CompanyId == companyId && b.Status == BatchStatus.Posted))
                group pa by pa.InvoiceId into g
                select new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken + x.WithholdingAmountForeign) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            var creditNotesMap = await InvoiceFinancialBalances.SalesCreditsAsync(ctx, invoiceIds, asOfDate: end);
            var cashRefundsMap = await InvoiceFinancialBalances.SalesCashRefundsAsync(ctx, invoiceIds, end);
            foreach (var id in payments.Keys.ToList()) payments[id] = Math.Max(0, payments[id] - cashRefundsMap.GetValueOrDefault(id));

            var reportData = new StandardReportData
            {
                ReportName = "Customer Transaction Report",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string> {
            "Date", "Invoice #", "Customer", "Item", "Qty", "Unit Price", "Line Total",
            "Order Discount", "Order Tax", "Credit Applied", "Net Invoice Total", "Amount Paid"
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
                decimal paidBase = Math.Round(paidForeign * rate, 2);

                // FIXED: Replaced undeclared discountedSubTotal with netForeign variable to fix compilation error
                decimal rawGrandTotal = netForeign + taxForeign;
                decimal creditedForeign = creditNotesMap.TryGetValue(o.Id, out var credAmt) ? credAmt : 0;
                decimal discountBase = Math.Round(discountForeign * rate, 2);
                decimal taxBase = Math.Round(taxForeign * rate, 2);
                decimal creditAppliedBase = Math.Round(creditedForeign * rate, 2);
                decimal netGrandTotalBase = Math.Round(rawGrandTotal * rate, 2) - creditAppliedBase;

                bool isFirstLine = true;
                foreach (var line in o.Lines)
                {
                    decimal unitPriceBase = Math.Round(line.UnitPrice * rate, 2);
                    decimal lineTotalBase = Math.Round(line.Quantity * line.UnitPrice * rate, 2);
                    var row = new List<string>
            {
                isFirstLine ? o.Date.ToString("yyyy-MM-dd") : "",
                isFirstLine ? o.OrderNumber : "",
                isFirstLine ? (o.Customer?.Name ?? "Unknown") : "",
                line.Item?.Name ?? line.Description ?? "Invoice line",
                line.Quantity.ToString("N2"),
                unitPriceBase.ToString("N2"),
                lineTotalBase.ToString("N2"),
                isFirstLine ? discountBase.ToString("N2") : "",
                isFirstLine ? taxBase.ToString("N2") : "",
                // FIXED: Mapping the actual credit adjustments onto the data grid rows row-by-row
                isFirstLine ? (creditAppliedBase > 0 ? $"({creditAppliedBase.ToString("N2")})" : "0.00") : "",
                isFirstLine ? netGrandTotalBase.ToString("N2") : "",
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
                .Where(o => o.CompanyId == companyId
                         && !ctx.SalesInvoices.Any(documentInvoice => documentInvoice.CompanyId == companyId && documentInvoice.Id == o.Id)
                         && o.Date <= asOfDate
                         && (o.IsDirectInvoice || o.InvoiceBatchId != null || o.OrderNumber.StartsWith("INV"))
                         && (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced))
                .ToListAsync();

            var documentInvoices = await ctx.SalesInvoices.AsNoTracking()
                .Include(x => x.Customer).Include(x => x.Currency).Include(x => x.Lines).ThenInclude(x => x.Item)
                .Where(x => x.CompanyId == companyId && x.Date <= asOfDate
                    && (x.Status == SalesInvoiceStatus.Posted || x.Status == SalesInvoiceStatus.InReview)
                    && ((!x.GLBatchId.HasValue && x.Status == SalesInvoiceStatus.Posted) || (x.GLBatchId.HasValue
                        && ctx.GLBatches.Any(batch => batch.Id == x.GLBatchId.Value && batch.CompanyId == companyId && batch.Status == BatchStatus.Posted))))
                .ToListAsync();
            orders.AddRange(documentInvoices.Select(ToSalesOrderReportRecord));

            var invoiceIds = orders.Select(o => o.Id).ToList();

            var payments = await (from pa in ctx.PaymentApplications join payment in ctx.CustomerPayments on pa.CustomerPaymentId equals payment.Id
                where invoiceIds.Contains(pa.InvoiceId) && payment.CompanyId == companyId && payment.Status == PaymentStatus.Posted
                    && payment.Date <= asOfDate.ToDateTime(TimeOnly.MaxValue)
                    && (!payment.GLBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == payment.GLBatchId && b.CompanyId == companyId && b.Status == BatchStatus.Posted))
                group pa by pa.InvoiceId into g
                select new { InvoiceId = g.Key, TotalPaid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken + x.WithholdingAmountForeign) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.TotalPaid);

            // FEATURE: Batch load credit notes to correctly calculate aging buckets
            var creditNotesMap = await InvoiceFinancialBalances.SalesCreditsAsync(ctx, invoiceIds, asOfDate: asOfDate);
            var cashRefundsMap = await InvoiceFinancialBalances.SalesCashRefundsAsync(ctx, invoiceIds, asOfDate);
            foreach (var id in payments.Keys.ToList()) payments[id] = Math.Max(0, payments[id] - cashRefundsMap.GetValueOrDefault(id));

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
                    decimal grandTotalForeign = net + (net * (taxPer / 100));

                    decimal paidForeign = payments.TryGetValue(o.Id, out var paidAmt) ? paidAmt : 0;
                    decimal creditedForeign = creditNotesMap.TryGetValue(o.Id, out var creditedAmt) ? creditedAmt : 0;
                    decimal rate = o.ExchangeRate > 0 ? o.ExchangeRate : 1m;
                    decimal grandTotal = Math.Round(grandTotalForeign * rate, 2);
                    decimal paid = Math.Round(paidForeign * rate, 2);
                    decimal credited = Math.Round(creditedForeign * rate, 2);
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
                         && !ctx.SalesInvoices.Any(documentInvoice => documentInvoice.CompanyId == companyId && documentInvoice.Id == l.Header.Id)
                         && l.Header.Date >= start
                         && l.Header.Date <= end
                         && (l.Header.IsDirectInvoice || l.Header.InvoiceBatchId != null || l.Header.OrderNumber.StartsWith("INV"))
                         && validStatuses.Contains(l.Header.Status));

            var lines = await query.ToListAsync();
            var documentInvoices = await ctx.SalesInvoices.AsNoTracking()
                .Include(x => x.Customer).Include(x => x.Lines).ThenInclude(x => x.Item)
                .Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end
                    && (x.Status == SalesInvoiceStatus.Posted || x.Status == SalesInvoiceStatus.InReview)
                    && ((!x.GLBatchId.HasValue && x.Status == SalesInvoiceStatus.Posted) || (x.GLBatchId.HasValue
                        && ctx.GLBatches.Any(batch => batch.Id == x.GLBatchId.Value && batch.CompanyId == companyId && batch.Status == BatchStatus.Posted))))
                .ToListAsync();
            foreach (var invoiceDocument in documentInvoices)
            {
                var header = ToSalesOrderReportRecord(invoiceDocument);
                lines.AddRange(invoiceDocument.Lines.Where(x => x.ItemId.HasValue).Select(x => new SalesOrderLine
                { DimensionSetId = x.DimensionSetId,
                    Id = x.SalesOrderLineId ?? x.Id,
                    HeaderId = header.Id,
                    Header = header,
                    ItemId = x.ItemId,
                    Item = x.Item,
                    Description = x.Description,
                    Quantity = x.QuantityInvoiced,
                    UnitPrice = x.UnitPrice,
                    QtyInvoiced = x.QuantityInvoiced,
                    UomId = x.UomId,
                    UomName = x.UomName,
                    UomConversionFactor = x.UomConversionFactor
                }));
            }

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
                .Where(cl => (cl.SalesOrderLineId.HasValue && lineIds.Contains(cl.SalesOrderLineId.Value)) && cl.Header!.Status == CreditNoteStatus.Posted)
                .Where(cl => cl.SalesOrderLineId.HasValue).GroupBy(cl => cl.SalesOrderLineId!.Value)
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

        private static SalesOrder ToSalesOrderReportRecord(SalesInvoice invoice) => new()
        {
            Id = invoice.Id,
            CompanyId = invoice.CompanyId,
            CustomerId = invoice.CustomerId,
            Customer = invoice.Customer,
            CurrencyId = invoice.CurrencyId,
            Currency = invoice.Currency,
            WarehouseId = invoice.WarehouseId ?? Guid.Empty,
            ProjectId = invoice.ProjectId,
            OrderNumber = invoice.InvoiceNumber,
            Date = invoice.Date,
            TransactionDateTime = invoice.TransactionDateTime,
            Status = invoice.Status == SalesInvoiceStatus.Cancelled ? OrderStatus.Cancelled : OrderStatus.Invoiced,
            IsDirectInvoice = invoice.IsDirectInvoice,
            ExchangeRate = invoice.ExchangeRate,
            TaxId = invoice.TaxId,
            TaxGLAccountId = invoice.TaxGLAccountId,
            DiscountPercentage = invoice.DiscountPercentage,
            DiscountAmount = invoice.DiscountAmount,
            DiscountGlAccountId = invoice.DiscountGlAccountId,
            WithholdingPercentage = invoice.WithholdingPercentage,
            WithholdingSalesGlAccountId = invoice.WithholdingSalesGlAccountId,
            WithholdingTaxId = invoice.WithholdingTaxId,
            WithholdingTaxName = invoice.WithholdingTaxName,
            InvoiceBatchId = invoice.InvoiceBatchId ?? invoice.GLBatchId,
            GrandTotalForeign = invoice.TotalAmountForeign,
            Lines = invoice.Lines.Select(line => new SalesOrderLine
            { DimensionSetId = line.DimensionSetId,
                Id = line.SalesOrderLineId ?? line.Id,
                HeaderId = invoice.Id,
                ItemId = line.ItemId,
                Item = line.Item,
                Description = line.Description,
                Quantity = line.QuantityInvoiced,
                UnitPrice = line.UnitPrice,
                QtyInvoiced = line.QuantityInvoiced,
                UomId = line.UomId,
                UomName = line.UomName,
                UomConversionFactor = line.UomConversionFactor
            }).ToList()
        };
    }
}
