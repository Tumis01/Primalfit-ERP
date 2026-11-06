using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public partial class PurchasingService
    {
        private static bool HasTransientSqlError(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
                if (current is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 1205 || sql.Number == 1222 || sql.Number == -2)) return true;
            return false;
        }
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly InventoryService _inventoryService;
        private readonly GLOperationsService _glOps;
        private readonly BudgetService _budgetService;
        private readonly InventoryValuationService _valuationService;
        private readonly TransactionMappingService _mappingService; // <-- NEW: Injected Mapping Service
        private readonly IDocumentNumberingService _numberingService;

        public PurchasingService(
            IDbContextFactory<AppDbContext> dbFactory,
        InventoryService inventoryService,
        GLOperationsService glOps,
        BudgetService budgetService,
        InventoryValuationService valuationService,
        TransactionMappingService mappingService,
        IDocumentNumberingService numberingService) // <-- NEW
        {
            _dbFactory = dbFactory;
            _inventoryService = inventoryService;
            _glOps = glOps;
            _budgetService = budgetService;
            _valuationService = valuationService;
            _mappingService = mappingService; // <-- NEW
            _numberingService = numberingService;
        }

        public async Task<string> SavePurchaseInvoiceAsync(PurchaseInvoice bill)
        {
            if (bill.MatchVarianceReason == null) bill.MatchVarianceReason = "";
            if (bill.ExternalInvoiceNumber == null) bill.ExternalInvoiceNumber = "";

            if (bill.CompanyId == Guid.Empty) return "System Error: Bill has no Company ID.";
            if (bill.VendorId == Guid.Empty) return "Vendor is required.";
            if (bill.Lines == null || bill.Lines.Count == 0) return "Bill must have at least one line.";

            using var ctx = await _dbFactory.CreateDbContextAsync();

            // --- CRITICAL DUPLICATE CHECK ---
            // A vendor invoice number must be unique per Vendor per Company
            if (!string.IsNullOrWhiteSpace(bill.ExternalInvoiceNumber))
            {
                bool invoiceExists = await ctx.PurchaseInvoices
                    .AnyAsync(b => b.CompanyId == bill.CompanyId
                                && b.VendorId == bill.VendorId
                                && b.ExternalInvoiceNumber.ToLower() == bill.ExternalInvoiceNumber.ToLower()
                                && b.Id != bill.Id); // Exclude self if updating an existing draft

                if (invoiceExists)
                    return $"STOP: Invoice number '{bill.ExternalInvoiceNumber}' has already been recorded for this vendor.";
            }

            var existing = await ctx.PurchaseInvoices
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == bill.Id);

            var billCurrency = existing?.VendorId == bill.VendorId ? existing.CurrencyId
                : await ctx.Vendors.Where(x => x.Id == bill.VendorId && x.CompanyId == bill.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            var billCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(ctx, bill.CompanyId, bill.CurrencyId, bill.ExchangeRate, billCurrency);
            if (billCurrencyError != null) return billCurrencyError;

            try
            {
                if (existing == null)
                {
                    if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();

                    foreach (var line in bill.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.PurchaseInvoiceId = bill.Id;
                    }

                    ctx.PurchaseInvoices.Add(bill);
                    ctx.PurchaseInvoiceLines.AddRange(bill.Lines);
                }
                else
                {
                    var existingBatch = existing.GLBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == existing.GLBatchId.Value && b.CompanyId == existing.CompanyId)
                        : null;
                    if (existing.IsPosted && (existingBatch == null || existingBatch.Status == BatchStatus.Posted))
                        return "STOP: Cannot edit a bill that has already been posted.";

                    bill.CompanyId = existing.CompanyId;
                    bill.IsPosted = existing.IsPosted;
                    bill.PostedDate = existing.PostedDate;

                    ctx.Entry(existing).CurrentValues.SetValues(bill);
                    ctx.PurchaseInvoiceLines.RemoveRange(existing.Lines);

                    foreach (var line in bill.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.PurchaseInvoiceId = bill.Id;
                    }
                    ctx.PurchaseInvoiceLines.AddRange(bill.Lines);
                }

                if (!await ctx.Vendors.AnyAsync(v => v.Id == bill.VendorId))
                    return "STOP: Selected Vendor does not exist.";

                if (bill.PurchaseOrderId.HasValue && bill.PurchaseOrderId != Guid.Empty)
                {
                    if (!await ctx.PurchaseOrders.AnyAsync(p => p.Id == bill.PurchaseOrderId))
                        return "STOP: Linked Purchase Order does not exist.";
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"DATABASE ERROR: {ex.Message}";
            }
        }

        public async Task<string> ValidateThreeWayMatch(Guid billId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var bill = await ctx.PurchaseInvoices
                .Include(b => b.Lines)
                    .ThenInclude(l => l.GoodsReceiptLine)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null) return "Purchase invoice not found.";

            if (bill.PurchaseOrderId == null)
            {
                bill.MatchStatus = BillMatchStatus.NoPoLinked;
                await ctx.SaveChangesAsync();
                return string.Empty;
            }

            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value);

            if (po == null) return "Purchase Order not found.";

            var receipts = await ctx.GoodsReceipts
                .Include(g => g.Lines)
                .Where(g => g.PurchaseOrderId == bill.PurchaseOrderId.Value)
                .ToListAsync();

            decimal totalReceivedValueBase = 0;

            foreach (var grn in receipts)
            {
                foreach (var line in grn.Lines)
                {
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                    if (poLine != null)
                    {
                        decimal receivedForeign = line.QuantityReceived * poLine.UnitCost;
                        decimal receivedBase = receivedForeign * po.ExchangeRate;
                        totalReceivedValueBase += receivedBase;
                    }
                }
            }

            decimal tolerance = 1.00m;
            decimal variance = bill.TotalAmount - totalReceivedValueBase;

            if (variance > tolerance)
            {
                bill.MatchStatus = BillMatchStatus.Variance;
                bill.MatchVarianceReason =
                    $"Bill Total ({bill.TotalAmount:C}) exceeds Value of Goods Received ({totalReceivedValueBase:C}). Variance: {variance:C}";
                await ctx.SaveChangesAsync();
                return bill.MatchVarianceReason;
            }

            bill.MatchStatus = BillMatchStatus.Matched;
            bill.MatchVarianceReason = "";

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 1. SAVE PURCHASE ORDER
        public async Task<string> SavePurchaseOrderAsync(PurchaseOrder po)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            // Protect order-line edits against receipt reservations created concurrently.
            await using var sourceTransaction = await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            if (po.CompanyId == Guid.Empty) return "Company is required.";
            if (po.VendorId == Guid.Empty) return "Vendor is required.";
            if (po.Lines.Count == 0) return "Order must have at least one line.";
            if (po.Lines.Any(l => l.ItemId == Guid.Empty || l.QuantityOrdered <= 0 || l.UnitCost < 0)) return "Each order line needs an item, positive quantity and non-negative cost.";
            if (!await ctx.Vendors.AnyAsync(v => v.Id == po.VendorId && v.CompanyId == po.CompanyId)) return "Vendor is not available in this company.";
            if (!await ctx.Currencies.AnyAsync(c => c.Id == po.CurrencyId && c.CompanyId == po.CompanyId)) return "Currency is not available in this company.";
            var orderCurrency = await ctx.PurchaseOrders.Where(x => x.Id == po.Id && x.CompanyId == po.CompanyId && x.VendorId == po.VendorId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            if (!orderCurrency.HasValue && !po.Lines.Any(x => x.PurchaseRequestLineId.HasValue)) orderCurrency = await ctx.Vendors.Where(x => x.Id == po.VendorId && x.CompanyId == po.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
            var orderCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(ctx, po.CompanyId, po.CurrencyId, po.ExchangeRate, orderCurrency);
            if (orderCurrencyError != null) return orderCurrencyError;
            if (po.WarehouseId.HasValue && !await ctx.Warehouses.AnyAsync(w => w.Id == po.WarehouseId && w.CompanyId == po.CompanyId && (w.Type == WarehouseType.Purchase || w.Type == WarehouseType.Both))) return "Site is not available for purchasing in this company.";

            var itemIds = po.Lines.Where(x => x.ItemId != Guid.Empty).Select(x => x.ItemId).Distinct().ToList();
            var itemMap = await ctx.Items.Include(x => x.UomConversions).Where(x => itemIds.Contains(x.Id) && x.CompanyId == po.CompanyId).ToDictionaryAsync(x => x.Id);
            if (itemMap.Count != itemIds.Count) return "An order item is unavailable in this company.";
            var conversionMap = await ctx.UomConversionRules.AsNoTracking()
                .Where(x => x.CompanyId == po.CompanyId && x.IsActive)
                .ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var companyUomIds = await ctx.UnitOfMeasures.AsNoTracking()
                .Where(x => x.CompanyId == po.CompanyId).Select(x => x.Id).ToHashSetAsync();
            var savedUoms = await ctx.PurchaseOrderLines.AsNoTracking().Where(x => x.PurchaseOrderId == po.Id && ctx.PurchaseOrders.Any(p => p.Id == x.PurchaseOrderId && p.CompanyId == po.CompanyId)).ToDictionaryAsync(x => x.Id);
            var requestIds = po.Lines.Where(x => x.PurchaseRequestLineId.HasValue).Select(x => x.PurchaseRequestLineId!.Value).ToList();
            var requestUoms = await ctx.PurchaseRequestLines.AsNoTracking().Where(x => requestIds.Contains(x.Id) && x.PurchaseRequest!.CompanyId == po.CompanyId).ToDictionaryAsync(x => x.Id);
            foreach (var line in po.Lines.Where(x => itemMap.ContainsKey(x.ItemId)))
            {
                var item = itemMap[line.ItemId];
                if (!line.UomId.HasValue) line.UomId = item.UomId;
                if (line.UomId.HasValue && !companyUomIds.Contains(line.UomId.Value)) return "Selected line UOM is invalid.";
                if (savedUoms.TryGetValue(line.Id, out var savedUnit) && savedUnit.ItemId == line.ItemId && savedUnit.UomId == line.UomId && savedUnit.UomConversionFactor == line.UomConversionFactor && line.UomConversionFactor > 0) continue;
                if (line.PurchaseRequestLineId.HasValue && requestUoms.TryGetValue(line.PurchaseRequestLineId.Value, out var requestUnit) && requestUnit.ItemId == line.ItemId && requestUnit.UomId == line.UomId && requestUnit.UomConversionFactor == line.UomConversionFactor && line.UomConversionFactor > 0) continue;
                if (line.UomId.HasValue && line.UomId != item.UomId
                    && !UomConversion.IsConfigured(item, line.UomId))
                    return $"The selected UOM for '{item.Name}' is not configured on this item.";
                var expected = UomConversion.FactorFor(item, line.UomId, conversionMap);
                if (line.UomConversionFactor <= 0 || Math.Abs(line.UomConversionFactor - expected) > 0.000000000000001m)
                    line.UomConversionFactor = expected;
            }

            if (po.WithholdingTaxId.HasValue)
            {
                var withholding = await ctx.WithholdingTaxes.AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Id == po.WithholdingTaxId.Value && w.CompanyId == po.CompanyId);
                if (withholding == null) return "Selected withholding tax is not configured for this company.";
                if (withholding.WithholdingGlAccountId == Guid.Empty) return "Selected withholding tax has no purchase GL account mapped.";
                if (!await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == withholding.WithholdingGlAccountId && a.CompanyId == po.CompanyId && a.IsActive))
                    return "Selected withholding tax purchase account is invalid or inactive.";
                po.WithholdingTaxName = withholding.Name;
                po.WithholdingPercentage = Math.Round(withholding.PercentageValue, 4);
                po.WithholdingGlAccountId = withholding.WithholdingGlAccountId;
            }
            else
            {
                po.WithholdingTaxId = null;
                po.WithholdingTaxName = null;
                po.WithholdingPercentage = 0;
                po.WithholdingAmountForeign = 0;
                po.WithholdingAmount = 0;
                po.WithholdingGlAccountId = null;
            }

            var existing = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == po.Id);

            var dimensionError = await DimensionService.ValidateAsync(ctx, po.CompanyId, po.Lines.Select(x => x.DimensionSetId), po.Status != PurchaseOrderStatus.DraftInvoice);
            if (dimensionError.Length > 0) return dimensionError;
            var sourceError = await OrderSourceValidation.PreparePurchaseAsync(ctx, po);
            if (sourceError != null) return sourceError;

            try
            {
                if (existing == null)
                {
                    if (po.Id == Guid.Empty) po.Id = Guid.NewGuid();

                    // --- GENERATE NUMBER BASED ON STATUS WITH COLLISION LOOP ---
                    if (string.IsNullOrWhiteSpace(po.OrderNumber))
                    {
                        var target = po.Status switch
                        {
                            PurchaseOrderStatus.Request => SequenceTargetType.AP_Request,
                            PurchaseOrderStatus.DraftInvoice => SequenceTargetType.AP_Invoice,
                            _ => SequenceTargetType.AP_PurchaseOrder
                        };
                        po.OrderNumber = await _numberingService.GenerateAndReserveNextNumberAsync(po.CompanyId, target, po.OrderDate);
                    }

                    foreach (var line in po.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.PurchaseOrderId = po.Id;
                    }
                    ctx.PurchaseOrders.Add(po);
                }
                else
                {
                    var existingBatch = existing.GLBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking()
                            .FirstOrDefaultAsync(b => b.Id == existing.GLBatchId.Value && b.CompanyId == existing.CompanyId)
                        : null;
                    bool reviewEditable = existingBatch?.Status is BatchStatus.Ready or BatchStatus.Rejected or BatchStatus.Draft;

                    if (!reviewEditable && (existing.IsInvoicePosted || existing.HasReceipt))
                        return "Cannot edit an order that has already been received or invoiced.";
                    var existingOrderLineIds = existing.Lines.Select(x => x.Id).ToList();
                    if (await ctx.GoodsReceiptDraftLines.AnyAsync(x => x.PurchaseOrderLineId.HasValue && existingOrderLineIds.Contains(x.PurchaseOrderLineId.Value)))
                        return "This order has quantities reserved by a goods receipt draft. Submit or delete the draft before editing the order.";

                    if (existing.Status == PurchaseOrderStatus.Request)
                    {
                        bool isConverted = await ctx.PurchaseOrders.AnyAsync(p => p.ConvertedFromRequestNumber == existing.OrderNumber);
                        if (isConverted) return "Cannot edit a Request that has already been converted to an Order.";
                    }

                    if (po.CompanyId != existing.CompanyId) return "Cannot change an order's company.";
                    if (existing.Lines.Any(l => l.PurchaseRequestLineId.HasValue) && (po.VendorId != existing.VendorId || po.CurrencyId != existing.CurrencyId))
                        return "Supplier and currency cannot be changed on an order created from request lines.";
                    if (po.Lines.Where(l => l.Id != Guid.Empty).GroupBy(l => l.Id).Any(g => g.Count() > 1)) return "An order line cannot be repeated.";
                    foreach (var sourceLine in existing.Lines.Where(l => l.PurchaseRequestLineId.HasValue))
                    {
                        var edited = po.Lines.SingleOrDefault(l => l.Id == sourceLine.Id);
                        if (edited == null || edited.PurchaseRequestLineId != sourceLine.PurchaseRequestLineId)
                            return "A sourced request line cannot be removed or have its source changed.";
                        edited.PurchaseRequestLineId = sourceLine.PurchaseRequestLineId;
                    }
                    po.CompanyId = existing.CompanyId;
                    po.OrderNumber = existing.OrderNumber;
                    po.ConvertedFromRequestNumber = existing.ConvertedFromRequestNumber;

                    ctx.Entry(existing).CurrentValues.SetValues(po);
                    var existingLines = existing.Lines.ToDictionary(l => l.Id);
                    var incomingIds = po.Lines.Select(l => l.Id).ToHashSet();
                    ctx.PurchaseOrderLines.RemoveRange(existing.Lines.Where(l => !incomingIds.Contains(l.Id)));
                    foreach (var line in po.Lines)
                    {
                        if (existingLines.TryGetValue(line.Id, out var persisted))
                        {
                            var received = persisted.QuantityReceived;
                            var billed = persisted.QuantityBilled;
                            var requestLineId = persisted.PurchaseRequestLineId;
                            ctx.Entry(persisted).CurrentValues.SetValues(line);
                            persisted.PurchaseOrderId = existing.Id;
                            persisted.PurchaseRequestLineId = requestLineId ?? line.PurchaseRequestLineId;
                            persisted.QuantityReceived = received;
                            persisted.QuantityBilled = billed;
                        }
                        else
                        {
                            if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                            line.PurchaseOrderId = existing.Id;
                            ctx.PurchaseOrderLines.Add(line);
                        }
                    }
                }

                await ctx.SaveChangesAsync();
                await sourceTransaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"DATABASE ERROR: {ex.Message}";
            }
        }

        // 1B. CONVERT REQUEST TO PO
        public async Task<string> ConvertRequestToOrderAsync(Guid requestId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var req = await ctx.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == requestId);

            if (req == null) return "Request not found.";
            if (req.Status != PurchaseOrderStatus.Request) return "Only Requests can be converted to Purchase Orders.";

            bool alreadyConverted = await ctx.PurchaseOrders.AnyAsync(o => o.ConvertedFromRequestNumber == req.OrderNumber);
            if (alreadyConverted) return "This Request has already been converted.";

            var generatedPoNumber = await _numberingService.GenerateAndReserveNextNumberAsync(req.CompanyId, SequenceTargetType.AP_PurchaseOrder);

            var order = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = req.CompanyId,
                OrderNumber = generatedPoNumber, // Assigned safely via the collision check
                ConvertedFromRequestNumber = req.OrderNumber,
                TaxId = req.TaxId,
                TaxGLAccountId = req.TaxGLAccountId,
                VendorId = req.VendorId,
                ProjectId = req.ProjectId,
                OrderDate = DateTime.Now,
                Status = PurchaseOrderStatus.Open,
                CurrencyId = req.CurrencyId,
                ExchangeRate = req.ExchangeRate,
                // Inherit Discounts
                DiscountPercentage = req.DiscountPercentage,
                DiscountAmount = req.DiscountAmount,
                DiscountGlAccountId = req.DiscountGlAccountId,
                WithholdingTaxId = req.WithholdingTaxId,
                WithholdingTaxName = req.WithholdingTaxName,
                WithholdingPercentage = req.WithholdingPercentage,
                WithholdingAmountForeign = req.WithholdingAmountForeign,
                WithholdingAmount = req.WithholdingAmount,
                WithholdingGlAccountId = req.WithholdingGlAccountId
            };

            foreach (var line in req.Lines)
            {
                order.Lines.Add(new PurchaseOrderLine
                { DimensionSetId = line.DimensionSetId,
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = order.Id,
                    ItemId = line.ItemId,
                    QuantityOrdered = line.QuantityOrdered,
                    UnitCost = line.UnitCost,
                    UomId = line.UomId,
                    UomName = line.UomName,
                    UomConversionFactor = line.UomConversionFactor
                });
            }

            ctx.PurchaseOrders.Add(order);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<string> ConvertPOToInvoiceAsync(Guid poId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == poId);

            if (po == null) return "Purchase Order not found.";
            if (po.Status == PurchaseOrderStatus.Request) return "Cannot invoice a Request directly. Convert it to a Purchase Order first.";

            // Prevent duplicate conversions up the pipeline chain
            bool alreadyConverted = await ctx.PurchaseOrders.AnyAsync(o => o.ConvertedFromPONumber == po.OrderNumber && o.CompanyId == po.CompanyId);
            if (alreadyConverted) return "This Purchase Order has already been converted to an invoice.";

            var generatedInvoiceNumber = await _numberingService.GenerateAndReserveNextNumberAsync(po.CompanyId, SequenceTargetType.AP_Invoice);

            var invoice = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = po.CompanyId,
                OrderNumber = generatedInvoiceNumber, // Assigned safely via the collision check
                ConvertedFromPONumber = po.OrderNumber,
                TaxId = po.TaxId,
                TaxGLAccountId = po.TaxGLAccountId,
                VendorId = po.VendorId,
                ProjectId = po.ProjectId,
                OrderDate = DateTime.Now,
                Status = PurchaseOrderStatus.DraftInvoice, // Initial state inside the Invoice workspace
                CurrencyId = po.CurrencyId,
                ExchangeRate = po.ExchangeRate,
                DiscountPercentage = po.DiscountPercentage,
                DiscountAmount = po.DiscountAmount,
                DiscountGlAccountId = po.DiscountGlAccountId,
                WithholdingTaxId = po.WithholdingTaxId,
                WithholdingTaxName = po.WithholdingTaxName,
                WithholdingPercentage = po.WithholdingPercentage,
                WithholdingAmountForeign = po.WithholdingAmountForeign,
                WithholdingAmount = po.WithholdingAmount,
                WithholdingGlAccountId = po.WithholdingGlAccountId
            };

            foreach (var line in po.Lines)
            {
                invoice.Lines.Add(new PurchaseOrderLine
                { DimensionSetId = line.DimensionSetId,
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = invoice.Id,
                    ItemId = line.ItemId,
                    QuantityOrdered = line.QuantityOrdered,
                    UnitCost = line.UnitCost,
                    UomId = line.UomId,
                    UomName = line.UomName,
                    UomConversionFactor = line.UomConversionFactor
                });
            }

            ctx.PurchaseOrders.Add(invoice);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<string> PostInvoiceOrderAsync(Guid invoiceOrderId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var invoiceOrder = await ctx.PurchaseOrders
                    .Include(p => p.Lines)
                    .Include(p => p.CustomTransactionType)
                    .FirstOrDefaultAsync(p => p.Id == invoiceOrderId);

                if (invoiceOrder == null) return "Invoice record not found.";
                if (invoiceOrder.Status == PurchaseOrderStatus.Invoiced)
                {
                    var existingBatch = invoiceOrder.GLBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == invoiceOrder.GLBatchId.Value && b.CompanyId == invoiceOrder.CompanyId)
                        : null;
                    if (existingBatch?.Status == BatchStatus.Posted)
                        return "This invoice has already been committed to the General Ledger and cannot be modified.";
                    if (existingBatch == null)
                        return "This invoice is not linked to a review batch.";
                }

                bool isLegitDraft = invoiceOrder.Status == PurchaseOrderStatus.DraftInvoice ||
                                    invoiceOrder.OrderNumber.StartsWith("INV", StringComparison.OrdinalIgnoreCase);

                if (!isLegitDraft)
                    return $"Validation Exception: Only Draft Invoices can be posted. Current Status: {invoiceOrder.Status}";

                var grns = await ctx.GoodsReceipts
                    .Include(g => g.Lines)
                    .Where(g => g.PurchaseOrderId == invoiceOrderId)
                    .ToListAsync();

                bool requiresPhysicalReceipt = invoiceOrder.Lines.Any();

                if (requiresPhysicalReceipt && !grns.Any() && !invoiceOrder.IsDirectInvoice)
                {
                    return "3-Way Match Exception: No Goods Receipt found for this document. You must receive items inside the Invoice View first to balance inventory clearing accounts.";
                }

                // 1. Resolve custom mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (invoiceOrder.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == invoiceOrder.CompanyId && m.CustomTransactionTypeId == invoiceOrder.CustomTransactionTypeId.Value);
                }

                // 2. Resolve GR/IR Clearing Account (Debit side on bill)
                Guid resolvedClearingAccount = invoiceOrder.GoodsReceiptClearingGlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? (grns.Any() ? grns.First().InventoryGlAccountId : Guid.Empty);

                if (resolvedClearingAccount == Guid.Empty)
                {
                    resolvedClearingAccount = await _mappingService.GetMappedAccountAsync(
                        invoiceOrder.CompanyId,
                        SystemTransactionType.GoodsReceipt,
                        isDebit: false,
                        defaultAccountId: Guid.Empty);
                }

                var vendor = await ctx.Vendors.FindAsync(invoiceOrder.VendorId);
                if (vendor?.PayablesAccountId == null && invoiceOrder.AccountsPayableGlAccountId == null && customMapping?.OverrideCreditGlAccountId == null)
                    return "Accounts Payable configuration missing on Vendor Master Profile.";

                if (resolvedClearingAccount == Guid.Empty)
                    resolvedClearingAccount = vendor?.PayablesAccountId ?? Guid.Empty;

                // 3. Resolve Accounts Payable Liability Account (Credit side)
                Guid resolvedApAccount = invoiceOrder.AccountsPayableGlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? vendor?.PayablesAccountId
                    ?? Guid.Empty;

                if (resolvedApAccount == Guid.Empty)
                {
                    resolvedApAccount = await _mappingService.GetMappedAccountAsync(
                        invoiceOrder.CompanyId,
                        SystemTransactionType.PurchaseInvoice,
                        isDebit: false,
                        defaultAccountId: vendor?.PayablesAccountId ?? Guid.Empty);
                }

                if (resolvedApAccount == Guid.Empty)
                    return "Unable to resolve a valid Accounts Payable GL Account for this vendor invoice.";

                var existingBill = await ctx.PurchaseInvoices
                    .Include(b => b.Lines)
                    .FirstOrDefaultAsync(b => b.PurchaseOrderId == invoiceOrder.Id && b.CompanyId == invoiceOrder.CompanyId);

                var bill = new PurchaseInvoice
                {
                    Id = existingBill?.Id ?? Guid.NewGuid(),
                    CompanyId = invoiceOrder.CompanyId,
                    VendorId = invoiceOrder.VendorId,
                    PurchaseOrderId = invoiceOrder.Id,
                    ProjectId = invoiceOrder.ProjectId,
                    AccountsPayableGlId = resolvedApAccount,
                    ExternalInvoiceNumber = invoiceOrder.OrderNumber,
                    BillDate = DateTime.Now,
                    CurrencyId = invoiceOrder.CurrencyId,
                    ExchangeRate = invoiceOrder.ExchangeRate,
                    IsPosted = true,
                    PostedDate = DateTime.Now,
                    MatchStatus = BillMatchStatus.Matched,
                    WithholdingTaxId = invoiceOrder.WithholdingTaxId,
                    WithholdingTaxName = invoiceOrder.WithholdingTaxName,
                    WithholdingPercentage = invoiceOrder.WithholdingPercentage,
                    WithholdingGlAccountId = invoiceOrder.WithholdingGlAccountId
                };

                decimal totalGrossForeign = 0;

                foreach (var line in invoiceOrder.Lines)
                {
                    if (line.QuantityOrdered > 0)
                    {
                        bill.Lines.Add(new PurchaseInvoiceLine
                        { DimensionSetId = line.DimensionSetId,
                            Id = Guid.NewGuid(),
                            PurchaseInvoiceId = bill.Id,
                            ItemId = line.ItemId,
                            QuantityBilled = line.QuantityOrdered,
                            UnitCostBilled = line.UnitCost,
                            UomId = line.UomId,
                            UomName = line.UomName,
                            UomConversionFactor = line.UomConversionFactor,
                            ExpenseGlAccountId = resolvedClearingAccount
                        });
                        totalGrossForeign += line.LineTotal;
                        line.QuantityBilled = line.QuantityOrdered;
                    }
                }

                decimal discountForeign = invoiceOrder.DiscountAmount;
                if (invoiceOrder.DiscountPercentage > 0)
                {
                    discountForeign = totalGrossForeign * (invoiceOrder.DiscountPercentage / 100);
                }
                decimal netForeign = totalGrossForeign - discountForeign;

                if (invoiceOrder.WithholdingTaxId.HasValue)
                {
                    var withholding = await ctx.WithholdingTaxes.AsNoTracking()
                        .FirstOrDefaultAsync(w => w.Id == invoiceOrder.WithholdingTaxId.Value && w.CompanyId == invoiceOrder.CompanyId);
                    if (withholding == null) return "Selected withholding tax is no longer available.";
                    if (withholding.WithholdingGlAccountId == Guid.Empty) return "Selected withholding tax has no GL account mapped.";
                    invoiceOrder.WithholdingTaxName = withholding.Name;
                    invoiceOrder.WithholdingPercentage = Math.Round(withholding.PercentageValue, 4);
                    invoiceOrder.WithholdingGlAccountId = withholding.WithholdingGlAccountId;
                    invoiceOrder.WithholdingAmountForeign = Math.Round(netForeign * withholding.PercentageValue / 100m, 4);
                    invoiceOrder.WithholdingAmount = Math.Round(invoiceOrder.WithholdingAmountForeign * invoiceOrder.ExchangeRate, 4);
                    bill.WithholdingTaxName = invoiceOrder.WithholdingTaxName;
                    bill.WithholdingPercentage = invoiceOrder.WithholdingPercentage;
                    bill.WithholdingGlAccountId = invoiceOrder.WithholdingGlAccountId;
                    bill.WithholdingAmountForeign = invoiceOrder.WithholdingAmountForeign;
                    bill.WithholdingAmount = invoiceOrder.WithholdingAmount;
                }
                else
                {
                    invoiceOrder.WithholdingTaxName = null;
                    invoiceOrder.WithholdingPercentage = 0;
                    invoiceOrder.WithholdingAmountForeign = 0;
                    invoiceOrder.WithholdingAmount = 0;
                    invoiceOrder.WithholdingGlAccountId = null;
                    bill.WithholdingTaxId = null;
                    bill.WithholdingTaxName = null;
                    bill.WithholdingPercentage = 0;
                    bill.WithholdingAmountForeign = 0;
                    bill.WithholdingAmount = 0;
                    bill.WithholdingGlAccountId = null;
                }

                decimal taxForeign = 0;
                if (invoiceOrder.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(invoiceOrder.TaxId);
                    if (tax != null) taxForeign = netForeign * (tax.Per / 100);
                }

                bill.TotalAmountForeign = netForeign + taxForeign;

                decimal grossBase = Math.Round(totalGrossForeign * invoiceOrder.ExchangeRate, 2);
                decimal discountBase = Math.Round(discountForeign * invoiceOrder.ExchangeRate, 2);
                decimal taxBase = Math.Round(taxForeign * invoiceOrder.ExchangeRate, 2);
                decimal grandTotalBase = (grossBase - discountBase) + taxBase;

                bill.TotalAmount = grandTotalBase;

                var glLines = new List<GLJournalLine>();

                if (grossBase > 0)
                {
                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = resolvedClearingAccount,
                        Debit = grossBase,
                        Credit = 0,
                        Reference = $"Clear GR/IR: {bill.ExternalInvoiceNumber}"
                    });
                }

                if (taxBase > 0 && invoiceOrder.TaxGLAccountId.HasValue)
                {
                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = invoiceOrder.TaxGLAccountId.Value,
                        Debit = taxBase,
                        Credit = 0,
                        Reference = $"Input VAT: {bill.ExternalInvoiceNumber}"
                    });
                }

                if (discountBase > 0)
                {
                    Guid discountAccount = invoiceOrder.DiscountGlAccountId ?? Guid.Empty;
                    if (discountAccount == Guid.Empty)
                    {
                        discountAccount = await _mappingService.GetMappedAccountAsync(
                            invoiceOrder.CompanyId,
                            SystemTransactionType.DiscountReceived,
                            isDebit: false,
                            defaultAccountId: Guid.Empty);
                    }

                    if (discountAccount == Guid.Empty) return "Financial configuration missing: No Discount Received GL Account mapped.";
                    glLines.Add(new GLJournalLine { SegCoaId = discountAccount, Debit = 0, Credit = discountBase, Reference = $"Disc Received: {bill.ExternalInvoiceNumber}" });
                }

                glLines.Add(new GLJournalLine { SegCoaId = resolvedApAccount, Debit = 0, Credit = grandTotalBase, Reference = $"AP Liability: {bill.ExternalInvoiceNumber}" });

                var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(invoiceOrder.CompanyId, DateOnly.FromDateTime(bill.BillDate), "Purchase Invoice", $"Inv {bill.ExternalInvoiceNumber}", glLines, userId, existingBatchId: invoiceOrder.GLBatchId, inheritedProjectId: invoiceOrder.ProjectId);
                if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                if (batchId.HasValue) await _glOps.PostBatchAsync(invoiceOrder.CompanyId, batchId.Value, userId);
                invoiceOrder.GLBatchId = batchId;
                bill.GLBatchId = batchId;

                if (existingBill == null)
                {
                    ctx.PurchaseInvoices.Add(bill);
                    ctx.PurchaseInvoiceLines.AddRange(bill.Lines);
                }
                else
                {
                    ctx.Entry(existingBill).CurrentValues.SetValues(bill);
                    ctx.PurchaseInvoiceLines.RemoveRange(existingBill.Lines);
                    foreach (var line in bill.Lines)
                    {
                        line.Id = Guid.NewGuid();
                        line.PurchaseInvoiceId = existingBill.Id;
                        ctx.PurchaseInvoiceLines.Add(line);
                    }
                }

                invoiceOrder.Status = PurchaseOrderStatus.Invoiced;
                invoiceOrder.IsInvoicePosted = true;

                if (!string.IsNullOrEmpty(invoiceOrder.ConvertedFromPONumber))
                {
                    var parentPo = await ctx.PurchaseOrders.FirstOrDefaultAsync(p => p.OrderNumber == invoiceOrder.ConvertedFromPONumber && p.CompanyId == invoiceOrder.CompanyId);
                    if (parentPo != null)
                    {
                        parentPo.IsInvoicePosted = true;
                        parentPo.Status = PurchaseOrderStatus.Invoiced;
                    }
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Procurement Posting Error: {ex.Message}";
            }
        }

        public async Task<string> SaveGoodsReceiptAsync(GoodsReceipt grn, Guid warehouseId, string userId, Guid? requiredDraftId = null, bool updatingReview = false)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            try
            {
                GoodsReceiptDraft? draft = null;
                Dictionary<Guid, decimal>? originalQuantities = null;
                if (updatingReview)
                {
                    var update = await PrepareReceiptReviewUpdateAsync(ctx, grn);
                    if (!string.IsNullOrEmpty(update.Error)) return update.Error;
                    grn = update.Receipt!;
                    originalQuantities = update.OriginalQuantities;
                    warehouseId = grn.WarehouseId ?? Guid.Empty;
                }
                else if (requiredDraftId.HasValue)
                {
                    draft = await ctx.GoodsReceiptDrafts.Include(x => x.Lines)
                        .FirstOrDefaultAsync(x => x.Id == requiredDraftId && x.CompanyId == grn.CompanyId);
                    if (draft == null) return "Receipt draft was not found or has already been submitted.";
                    grn = GoodsReceiptDraftService.ToReceipt(draft);
                    warehouseId = draft.WarehouseId;
                }
                else if (await ctx.GoodsReceiptDrafts.AnyAsync(x => x.CompanyId == grn.CompanyId && (x.Id == grn.Id || (!string.IsNullOrWhiteSpace(grn.GrnNumber) && x.GrnNumber == grn.GrnNumber))))
                    return "This receipt number belongs to a saved draft. Submit the draft from the goods receipt register.";
                if (grn.CompanyId == Guid.Empty) return "STOP: No CompanyId.";
                if (grn.Lines == null || grn.Lines.Count == 0) return "STOP: Select at least one purchase order line.";
                if (grn.Lines.Any(l => l.QuantityReceived < 0)) return "STOP: Receipt quantities cannot be negative.";
                grn.Lines = grn.Lines.Where(l => l.QuantityReceived > 0).ToList();
                if (grn.Lines.Count == 0) return "STOP: Enter a positive quantity on at least one receipt line.";
                if (warehouseId == Guid.Empty) return "STOP: No Warehouse selected.";
                if (!await ctx.Warehouses.AnyAsync(w => w.Id == warehouseId && w.CompanyId == grn.CompanyId &&
                    (w.Type == WarehouseType.Both || w.Type == WarehouseType.Purchase)))
                    return "STOP: Select a warehouse available to the purchase module.";

                if (grn.Lines.Any(x => x.PurchaseInvoiceLineId.HasValue))
                {
                    var invoiceError = await SaveInvoiceSourcedReceiptAsync(ctx, grn, draft, warehouseId, userId, updatingReview, originalQuantities);
                    if (!string.IsNullOrEmpty(invoiceError)) return invoiceError;
                    await transaction.CommitAsync();
                    return string.Empty;
                }

                if (grn.Lines.Any(l => !l.PurchaseOrderLineId.HasValue || l.PurchaseOrderLineId.Value == Guid.Empty))
                    return "STOP: Every receipt line must reference a valid purchase order line.";
                var requestedPoLineIds = grn.Lines.Select(l => l.PurchaseOrderLineId!.Value).Distinct().ToList();
                if (requestedPoLineIds.Count == 0) return "STOP: Select at least one valid purchase order line.";
                var sourcePoLines = await ctx.PurchaseOrderLines
                    .FromSqlRaw("SELECT * FROM [PurchaseOrderLines] WITH (UPDLOCK, HOLDLOCK)")
                    .Where(l => requestedPoLineIds.Contains(l.Id))
                    .ToListAsync();
                if (sourcePoLines.Count != requestedPoLineIds.Count)
                    return "STOP: One or more selected purchase order lines no longer exist.";
                var sourcePoIds = sourcePoLines.Select(l => l.PurchaseOrderId).Distinct().ToList();
                var sourceOrders = await ctx.PurchaseOrders.Include(p => p.Lines)
                    .Where(p => sourcePoIds.Contains(p.Id) && p.CompanyId == grn.CompanyId)
                    .ToListAsync();
                if (sourceOrders.Count != sourcePoIds.Count)
                    return "STOP: One or more selected purchase orders are missing or belong to another company.";
                if (sourceOrders.Select(p => p.VendorId).Distinct().Count() != 1)
                    return "STOP: A goods receipt can only combine purchase orders for the same supplier.";
                if (sourceOrders.Any(p => p.Status is PurchaseOrderStatus.Closed or PurchaseOrderStatus.Request
                    or PurchaseOrderStatus.DraftInvoice or PurchaseOrderStatus.Invoiced or PurchaseOrderStatus.Cancelled))
                    return "STOP: Only open purchase orders can be received.";
                var sourceOrderById = sourceOrders.ToDictionary(p => p.Id);
                var sourceLineById = sourcePoLines.ToDictionary(l => l.Id);
                grn.PurchaseOrderId = sourcePoIds.Count == 1 ? sourcePoIds[0] : null;
                grn.WarehouseId = warehouseId;
                grn.ProjectId = sourceOrders.Select(p => p.ProjectId).Distinct().Count() == 1
                    ? sourceOrders[0].ProjectId
                    : null;

                var conversionMap = await ctx.UomConversionRules.AsNoTracking()
                    .Where(x => x.CompanyId == grn.CompanyId && x.IsActive)
                    .ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
                var companyUomIds = await ctx.UnitOfMeasures.AsNoTracking()
                    .Where(x => x.CompanyId == grn.CompanyId).Select(x => x.Id).ToHashSetAsync();
                var receiptItemIds = sourcePoLines.Select(x => x.ItemId).Distinct().ToList();
                var itemFactors = await ctx.ItemUomConversionLines.AsNoTracking()
                    .Where(x => x.IsActive && receiptItemIds.Contains(x.ItemId))
                    .GroupBy(x => new { x.ItemId, x.UomId })
                    .ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
                // Validate each selected source line against all posted receipts so partial
                // receipts remain available, while a receipt cannot exceed the PO balance.
                var sourcePoLineIds = sourceOrders.SelectMany(p => p.Lines).Select(l => l.Id).Distinct().ToList();
                var pastReceipts = await ctx.GoodsReceiptLines
                    .Where(l => (!updatingReview || l.GoodsReceiptId != grn.Id) && l.PurchaseOrderLineId.HasValue && sourcePoLineIds.Contains(l.PurchaseOrderLineId.Value))
                    .ToListAsync();
                var otherReservations = await ctx.GoodsReceiptDraftLines
                    .Where(l => l.PurchaseOrderLineId.HasValue && sourcePoLineIds.Contains(l.PurchaseOrderLineId.Value) && (!requiredDraftId.HasValue || l.GoodsReceiptDraftId != requiredDraftId.Value))
                    .GroupBy(l => l.PurchaseOrderLineId!.Value).Select(g => new { Id = g.Key, Quantity = g.Sum(l => l.QuantityReceived) })
                    .ToDictionaryAsync(x => x.Id, x => x.Quantity);
                var adjustedQuantities = await InvoiceCorrectionBalances.PurchaseOrderAdjustmentsAsync(ctx, sourcePoLineIds);
                if (!grn.Lines.Any(l => l.QuantityReceived > 0))
                    return "STOP: Enter a positive quantity on at least one receipt line.";
                if (grn.Lines.Where(l => l.PurchaseOrderLineId.HasValue).GroupBy(l => l.PurchaseOrderLineId!.Value).Any(g => g.Count() > 1))
                    return "STOP: A purchase order line can only appear once on a goods receipt.";

                foreach (var line in grn.Lines)
                {
                    if (line.QuantityReceived < 0) return "STOP: Receipt quantities cannot be negative.";
                    if (!sourceLineById.TryGetValue(line.PurchaseOrderLineId!.Value, out var poLine))
                        return "STOP: A receipt line does not match a selected purchase order line.";
                    if (line.QuantityReceived == 0) continue;

                    var item = await ctx.Items.FindAsync(poLine.ItemId);
                    if (item == null) return $"STOP: Item master reference is missing for purchase line {poLine.Id}.";
                    if (item.CompanyId != grn.CompanyId) return $"STOP: Item '{item.Name}' belongs to another company.";

                    var factorsForItem = itemFactors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                    if (line.UomId.HasValue && (!companyUomIds.Contains(line.UomId.Value)
                        || (line.UomId != item.UomId && !factorsForItem.ContainsKey(line.UomId.Value)
                            && !UomConversion.IsConfigured(item, line.UomId))))
                        return $"STOP: The selected UOM for item '{item.Name}' is not configured.";
                    decimal expectedFactor = UomConversion.FactorFor(item, line.UomId, factorsForItem, conversionMap);
                    if (line.UomConversionFactor <= 0 || Math.Abs(line.UomConversionFactor - expectedFactor) > 0.000000000000001m)
                        return $"STOP: Invalid UOM conversion selected for item '{item.Name}'. Reload the receipt and try again.";

                    line.UomConversionFactor = expectedFactor;
                    line.UomName = string.IsNullOrWhiteSpace(line.UomName)
                        ? UomConversion.NameFor(item, line.UomId)
                        : line.UomName.Trim();

                    decimal pastQty = pastReceipts.Where(p => p.PurchaseOrderLineId == line.PurchaseOrderLineId).Sum(p => p.QuantityReceived);
                    decimal maxAllowed = Math.Max(0m, poLine.QuantityOrdered - pastQty - adjustedQuantities.GetValueOrDefault(poLine.Id) - otherReservations.GetValueOrDefault(poLine.Id));

                    if (UomConversion.Exceeds(line.QuantityReceived, maxAllowed)) return $"STOP: Cannot receive more than ordered for '{item.Name}'. Max allowed is {maxAllowed}.";
                    line.QuantityReceived = UomConversion.WithinRemaining(line.QuantityReceived, maxAllowed);
                }

                if (grn.Id == Guid.Empty) grn.Id = Guid.NewGuid();
                if (string.IsNullOrWhiteSpace(grn.GrnNumber))
                    grn.GrnNumber = await _numberingService.GenerateAndReserveNextNumberAsync(grn.CompanyId, SequenceTargetType.AP_GoodsReceipt, grn.DateReceived);

                if (!updatingReview) foreach (var line in grn.Lines) line.Id = Guid.NewGuid();

                // 1. Resolve custom template mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (grn.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == grn.CompanyId && m.CustomTransactionTypeId == grn.CustomTransactionTypeId.Value);
                }

                // 2. Resolve GR/IR Clearing Account (Credit Leg)
                Guid grIrClearingAccount = grn.OverrideGrIrClearingGlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? (grn.InventoryGlAccountId != Guid.Empty ? grn.InventoryGlAccountId : Guid.Empty);

                if (grIrClearingAccount == Guid.Empty)
                {
                    grIrClearingAccount = await _mappingService.GetMappedAccountAsync(
                        grn.CompanyId,
                        SystemTransactionType.GoodsReceipt,
                        isDebit: false,
                        defaultAccountId: Guid.Empty);
                }

                if (grIrClearingAccount == Guid.Empty)
                    return "STOP: You must configure a valid GR/IR Clearing Account in GL Mapping Settings or in the Route configuration.";

                grn.InventoryGlAccountId = grIrClearingAccount;

                if (!updatingReview) ctx.GoodsReceipts.Add(grn);

                var glLines = new List<GLJournalLine>();
                var stagedStockMovements = new List<StockLedger>();
                var originalStockValues = new Dictionary<Guid, decimal>();
                decimal totalReceivedValueBase = 0;

                // 3. Post Inventory & Financials
                var receivedQtyByItem = new Dictionary<Guid, decimal>();
                foreach (var grnLine in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    if (!sourceLineById.TryGetValue(grnLine.PurchaseOrderLineId!.Value, out var poLine)) continue;
                    var po = sourceOrderById[poLine.PurchaseOrderId];

                    var item = await ctx.Items.FindAsync(poLine.ItemId);
                    if (item == null) continue;

                    decimal lineValueForeign = grnLine.QuantityReceived * poLine.UnitCost;
                    decimal lineValueBase = Math.Round(lineValueForeign * po.ExchangeRate, 2);
                    totalReceivedValueBase += lineValueBase;

                    if (!item.IsService)
                    {
                        if (updatingReview) originalStockValues[item.Id] = originalStockValues.GetValueOrDefault(item.Id)
                            + Math.Round(originalQuantities![grnLine.Id] * poLine.UnitCost * po.ExchangeRate, 2);
                        // Inventory quantities and costs are maintained in the
                        // item's primary UOM and company base currency.
                        var priorQty = await ctx.StockLedgers
                            .Where(s => s.ItemId == item.Id)
                            .SumAsync(s => s.QuantityChanged);
                        priorQty += receivedQtyByItem.GetValueOrDefault(item.Id);
                        var oldWacc = item.WeightedAverageCost;
                        var incomingUnitCostBase = grnLine.QuantityReceived > 0
                            ? lineValueBase / grnLine.QuantityReceived
                            : 0m;
                        var newQty = priorQty + grnLine.QuantityReceived;
                        if (!updatingReview && newQty > 0)
                        {
                            item.WeightedAverageCost = Math.Round(
                                ((priorQty * oldWacc) + lineValueBase) / newQty, 4);
                            ctx.ItemCostHistories.Add(new ItemCostHistory
                            {
                                Id = Guid.NewGuid(), ItemId = item.Id,
                                OldQty = priorQty, OldWacc = oldWacc,
                                NewQtyIn = grnLine.QuantityReceived,
                                NewCostIn = incomingUnitCostBase,
                                ResultingWacc = item.WeightedAverageCost,
                                Reference = grn.GrnNumber,
                                DateChanged = DateTime.UtcNow
                            });
                        }
                        receivedQtyByItem[item.Id] = receivedQtyByItem.GetValueOrDefault(item.Id) + grnLine.QuantityReceived;

                        // A. Physical Stock Increase
                        var stockMovement = new StockLedger
                        { DimensionSetId = grnLine.DimensionSetId,
                            Id = Guid.NewGuid(),
                            CompanyId = grn.CompanyId,
                            WarehouseId = warehouseId,
                            ItemId = poLine.ItemId,
                            Date = grn.DateReceived,
                            Reference = grn.GrnNumber,
                            Type = StockMovementType.Purchase,
                            ProjectId = po.ProjectId,
                            // QuantityChanged is the canonical physical quantity in
                            // the item's primary/reference UOM.
                            QuantityChanged = grnLine.QuantityReceived,
                            UomId = grnLine.UomId,
                            UomName = string.IsNullOrWhiteSpace(grnLine.UomName) ? item.UoM : grnLine.UomName,
                            UomConversionFactor = UomConversion.NormalizeFactor(grnLine.UomConversionFactor),
                            QuantityInUom = UomConversion.FromBase(grnLine.QuantityReceived, grnLine.UomConversionFactor),
                            CostAtTime = incomingUnitCostBase
                        };
                        ctx.StockLedgers.Add(stockMovement);
                        stagedStockMovements.Add(stockMovement);

                        // B. Debit: Inventory Stock Asset
                        Guid itemInventoryAssetAccount = grn.OverrideInventoryAssetGlAccountId
                            ?? customMapping?.OverrideDebitGlAccountId
                            ?? await _mappingService.GetMappedAccountAsync(
                                grn.CompanyId,
                                SystemTransactionType.GoodsReceipt,
                                isDebit: true,
                                defaultAccountId: item.InventoryAssetAccountId);

                        if (itemInventoryAssetAccount == Guid.Empty)
                            return $"Configuration Error: Item '{item.Name}' is missing an Inventory Asset GL Account mapping.";

                        glLines.Add(new GLJournalLine
                        { DimensionSetId = grnLine.DimensionSetId, IsDimensionDetail = true,
                            SegCoaId = itemInventoryAssetAccount,
                            Debit = lineValueBase,
                            Credit = 0,
                            Reference = $"GRN Recv: {item.Name}"
                        });
                    }
                    else if (item.CostOfGoodsSoldAccountId != Guid.Empty)
                    {
                        // Debit: Expense (for Services)
                        glLines.Add(new GLJournalLine
                        { DimensionSetId = grnLine.DimensionSetId, IsDimensionDetail = true,
                            SegCoaId = item.CostOfGoodsSoldAccountId,
                            Debit = lineValueBase,
                            Credit = 0,
                            Reference = $"Service Recv: {item.Name}"
                        });
                    }
                }

                // 4. CREDIT: GR/IR CLEARING ACCOUNT (Temporary Liability)
                if (glLines.Any())
                {
                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = grIrClearingAccount,
                        Debit = 0,
                        Credit = totalReceivedValueBase,
                        Reference = $"GR/IR Accrual for {grn.GrnNumber}"
                    });

                    if (!updatingReview && grn.GLBatchId.HasValue) return "A new receipt cannot reuse an existing GL batch.";
                    if (updatingReview)
                    {
                        var stockError = await UpdateReceiptStockAsync(ctx, grn, stagedStockMovements, originalStockValues, userId);
                        if (!string.IsNullOrEmpty(stockError)) return stockError;
                    }
                    var (glErr, batchId) = updatingReview
                        ? await _glOps.RestageSubledgerBatchAsync(ctx, grn.CompanyId, grn.GLBatchId!.Value,
                            DateOnly.FromDateTime(grn.DateReceived), $"GRN {grn.GrnNumber}", grn.GrnNumber, glLines, userId, grn.ProjectId)
                        : await _glOps.StageNewSubledgerBatchAsync(
                        ctx,
                        grn.CompanyId,
                        DateOnly.FromDateTime(grn.DateReceived),
                        "Goods Receipt",
                        $"GRN {grn.GrnNumber}",
                        grn.GrnNumber,
                        glLines,
                        userId,
                        inheritedProjectId: grn.ProjectId);

                    if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                    if (stagedStockMovements.Count > 0 && !batchId.HasValue)
                        throw new Exception("GL staging did not create a review batch for this goods receipt.");
                    if (batchId.HasValue)
                    {
                        foreach (var stockMovement in stagedStockMovements)
                            stockMovement.GLBatchId = batchId;
                    }
                    grn.GLBatchId = batchId;
                }

                // Update each source PO independently. Receipts keep their own lines,
                // while the order header remains a legacy convenience reference.
                foreach (var po in sourceOrders)
                {
                    var poLineIds = po.Lines.Select(l => l.Id).ToList();
                    var receivedByLine = pastReceipts.Where(l => l.PurchaseOrderLineId.HasValue && poLineIds.Contains(l.PurchaseOrderLineId.Value))
                        .GroupBy(l => l.PurchaseOrderLineId!.Value)
                        .ToDictionary(g => g.Key, g => g.Sum(l => l.QuantityReceived));
                    foreach (var grnLine in grn.Lines.Where(l => l.PurchaseOrderLineId.HasValue && poLineIds.Contains(l.PurchaseOrderLineId.Value)))
                    {
                        receivedByLine[grnLine.PurchaseOrderLineId!.Value] = receivedByLine.GetValueOrDefault(grnLine.PurchaseOrderLineId!.Value) + grnLine.QuantityReceived;
                        var poLine = po.Lines.First(l => l.Id == grnLine.PurchaseOrderLineId);
                        poLine.QuantityReceived = receivedByLine[grnLine.PurchaseOrderLineId!.Value];
                    }
                    po.HasReceipt = grn.Lines.Any(l => l.PurchaseOrderLineId.HasValue && poLineIds.Contains(l.PurchaseOrderLineId.Value)) || po.HasReceipt;
                    po.IsFullyReceived = po.Lines.All(l => receivedByLine.GetValueOrDefault(l.Id, l.QuantityReceived) + UomConversion.QuantityTolerance >= l.QuantityOrdered);
                    if (po.Status is not (PurchaseOrderStatus.DraftInvoice or PurchaseOrderStatus.Invoiced or PurchaseOrderStatus.Closed))
                        po.Status = po.IsFullyReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
                }

                if (draft != null) ctx.GoodsReceiptDrafts.Remove(draft);
                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"DB ERROR: {ex.Message}";
            }
        }

        private async Task<string> SaveInvoiceSourcedReceiptAsync(AppDbContext ctx, GoodsReceipt grn, GoodsReceiptDraft? draft, Guid warehouseId, string userId, bool updatingReview = false, Dictionary<Guid, decimal>? originalQuantities = null)
        {
            if (grn.Lines.Any(x => !x.PurchaseInvoiceLineId.HasValue)
                || grn.Lines.Select(x => x.PurchaseInvoiceLineId).Distinct().Count() != grn.Lines.Count)
                return "Choose invoice lines only, with each line selected once.";
            var ids = grn.Lines.Select(x => x.PurchaseInvoiceLineId!.Value).ToList();
            var invoiceLines = await ctx.PurchaseInvoiceLines.FromSqlRaw("SELECT * FROM [PurchaseInvoiceLines] WITH (UPDLOCK, HOLDLOCK)")
                .Include(x => x.PurchaseInvoice).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (invoiceLines.Count != ids.Count || invoiceLines.Values.Any(x => x.PurchaseInvoice == null || x.PurchaseInvoice.CompanyId != grn.CompanyId)) return "An invoice source is unavailable.";
            var vendors = invoiceLines.Values.Select(x => x.PurchaseInvoice!.VendorId).Distinct().ToList();
            if (vendors.Count != 1 || (draft != null && draft.VendorId != vendors[0])) return "All invoice lines must belong to the selected supplier.";
            var roots = invoiceLines.Values.Where(x => x.PurchaseOrderLineId.HasValue).Select(x => x.PurchaseOrderLineId!.Value).ToList();
            if (roots.Distinct().Count() != roots.Count) return "A purchase order line cannot appear twice on one receipt.";
            var sourceLines = await ctx.PurchaseOrderLines.FromSqlRaw("SELECT * FROM [PurchaseOrderLines] WITH (UPDLOCK, HOLDLOCK)")
                .Where(x => roots.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var candidates = (await PurchaseInvoiceReceiptSources.GetAsync(ctx, grn.CompanyId, vendors[0], draft?.Id, updatingReview ? grn.Id : null)).ToDictionary(x => x.LineId);
            if (grn.Lines.Any(x => !candidates.ContainsKey(x.PurchaseInvoiceLineId!.Value))) return "An invoice source is no longer available. Refresh the receipt.";
            if (!updatingReview && await ctx.GoodsReceipts.AnyAsync(x => x.CompanyId == grn.CompanyId && (x.Id == grn.Id || x.GrnNumber == grn.GrnNumber))) return "This receipt has already been submitted or its number is in use.";
            var selected = grn.Lines.Select(x => candidates[x.PurchaseInvoiceLineId!.Value]).ToList();
            var clearingAccounts = selected.Select(x => x.ClearingAccountId).Distinct().ToList();
            if (clearingAccounts.Count != 1 || !clearingAccounts[0].HasValue || clearingAccounts[0] == Guid.Empty) return "Selected invoice lines must use the same recorded GR/IR clearing account.";
            var clearing = clearingAccounts[0]!.Value;
            if ((grn.OverrideGrIrClearingGlAccountId.HasValue && grn.OverrideGrIrClearingGlAccountId != clearing)
                || (grn.InventoryGlAccountId != Guid.Empty && grn.InventoryGlAccountId != clearing)) return "The receipt must use the clearing account recorded on the invoice.";
            var itemIds = selected.Select(x => x.ItemId).Distinct().ToList();
            var items = await ctx.Items.Include(x => x.UomConversions).Where(x => x.CompanyId == grn.CompanyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var uoms = await ctx.UnitOfMeasures.Where(x => x.CompanyId == grn.CompanyId).Select(x => x.Id).ToHashSetAsync();
            var rules = await ctx.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == grn.CompanyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var factors = await ctx.ItemUomConversionLines.AsNoTracking().Where(x => x.IsActive && itemIds.Contains(x.ItemId))
                .GroupBy(x => new { x.ItemId, x.UomId }).ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
            foreach (var line in grn.Lines)
            {
                var source = candidates[line.PurchaseInvoiceLineId!.Value]; var item = items[source.ItemId];
                if (line.PurchaseOrderLineId != source.OrderLineId || line.QuantityReceived <= 0 || UomConversion.Exceeds(line.QuantityReceived, source.Remaining)) return $"{item.Name}: the source or available quantity has changed. Refresh the receipt.";
                line.QuantityReceived = UomConversion.WithinRemaining(line.QuantityReceived, source.Remaining);
                var itemFactors = factors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                if (line.UomId.HasValue && (!uoms.Contains(line.UomId.Value) || (line.UomId != item.UomId && !itemFactors.ContainsKey(line.UomId.Value) && !UomConversion.IsConfigured(item, line.UomId)))) return $"The selected UOM for {item.Name} is not configured.";
                if (line.UomConversionFactor <= 0 || Math.Abs(line.UomConversionFactor - UomConversion.FactorFor(item, line.UomId, itemFactors, rules)) > 0.000000000000001m) return $"The UOM conversion for {item.Name} has changed.";
            }
            if (!updatingReview && grn.GLBatchId.HasValue) return "A new receipt cannot reuse an existing GL batch.";
            if (string.IsNullOrWhiteSpace(grn.GrnNumber)) grn.GrnNumber = await _numberingService.GenerateAndReserveNextNumberAsync(grn.CompanyId, SequenceTargetType.AP_GoodsReceipt, grn.DateReceived);
            var orderIds = sourceLines.Values.Select(x => x.PurchaseOrderId).Distinct().ToList();
            var orders = await ctx.PurchaseOrders.Include(x => x.Lines).Where(x => orderIds.Contains(x.Id)).ToListAsync();
            grn.PurchaseOrderId = orderIds.Count == 1 ? orderIds[0] : null; grn.WarehouseId = warehouseId; grn.InventoryGlAccountId = clearing;
            var invoiceProjects = invoiceLines.Values.Select(x => x.PurchaseInvoice!.ProjectId).Distinct().ToList();
            grn.ProjectId = invoiceProjects.Count == 1 ? invoiceProjects[0] : null;
            var glLines = new List<GLJournalLine>(); var movements = new List<StockLedger>(); var receivedByItem = new Dictionary<Guid, decimal>(); decimal total = 0;
            var originalStockValues = new Dictionary<Guid, decimal>();
            foreach (var line in grn.Lines)
            {
                var source = candidates[line.PurchaseInvoiceLineId!.Value]; var item = items[source.ItemId];
                var value = Math.Round(line.QuantityReceived * source.UnitCost * source.ExchangeRate, 2, MidpointRounding.AwayFromZero); total += value;
                if (value <= 0) return $"The invoiced cost of {item.Name} must be greater than zero for a goods receipt.";
                if (updatingReview) originalStockValues[item.Id] = originalStockValues.GetValueOrDefault(item.Id)
                    + Math.Round(originalQuantities![line.Id] * source.UnitCost * source.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                var prior = await ctx.StockLedgers.Where(x => x.CompanyId == grn.CompanyId && x.ItemId == item.Id).SumAsync(x => x.QuantityChanged);
                prior += receivedByItem.GetValueOrDefault(item.Id); var oldWacc = item.WeightedAverageCost; var newQuantity = prior + line.QuantityReceived;
                if (!updatingReview && newQuantity > 0)
                {
                    item.WeightedAverageCost = Math.Round((prior * oldWacc + value) / newQuantity, 4);
                    ctx.ItemCostHistories.Add(new ItemCostHistory { ItemId = item.Id, OldQty = prior, OldWacc = oldWacc, NewQtyIn = line.QuantityReceived,
                        NewCostIn = value / line.QuantityReceived, ResultingWacc = item.WeightedAverageCost, Reference = grn.GrnNumber, DateChanged = DateTime.UtcNow });
                }
                receivedByItem[item.Id] = receivedByItem.GetValueOrDefault(item.Id) + line.QuantityReceived;
                var movement = new StockLedger { DimensionSetId = line.DimensionSetId,  CompanyId = grn.CompanyId, WarehouseId = warehouseId, ItemId = item.Id, Date = grn.DateReceived,
                    Reference = grn.GrnNumber, Type = StockMovementType.Purchase, ProjectId = grn.ProjectId, QuantityChanged = line.QuantityReceived,
                    UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor,
                    QuantityInUom = UomConversion.FromBase(line.QuantityReceived, line.UomConversionFactor), CostAtTime = value / line.QuantityReceived };
                ctx.StockLedgers.Add(movement); movements.Add(movement);
                var asset = grn.OverrideInventoryAssetGlAccountId ?? await _mappingService.GetMappedAccountAsync(grn.CompanyId, SystemTransactionType.GoodsReceipt, true, item.InventoryAssetAccountId);
                if (asset == Guid.Empty) return $"Item '{item.Name}' needs an inventory asset account.";
                glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = asset, Debit = value, Reference = $"GRN Recv: {item.Name}" });
                if (!updatingReview) line.Id = Guid.NewGuid(); line.GoodsReceiptId = grn.Id;
            }
            glLines.Add(new GLJournalLine { SegCoaId = clearing, Credit = total, Reference = $"GR/IR Accrual for {grn.GrnNumber}" });
            if (updatingReview)
            {
                var stockError = await UpdateReceiptStockAsync(ctx, grn, movements, originalStockValues, userId);
                if (!string.IsNullOrEmpty(stockError)) return stockError;
            }
            var (error, batchId) = updatingReview
                ? await _glOps.RestageSubledgerBatchAsync(ctx, grn.CompanyId, grn.GLBatchId!.Value, DateOnly.FromDateTime(grn.DateReceived), $"GRN {grn.GrnNumber}", grn.GrnNumber, glLines, userId, grn.ProjectId)
                : await _glOps.StageNewSubledgerBatchAsync(ctx, grn.CompanyId, DateOnly.FromDateTime(grn.DateReceived), "Goods Receipt", $"GRN {grn.GrnNumber}", grn.GrnNumber, glLines, userId, grn.ProjectId);
            if (!string.IsNullOrEmpty(error) || !batchId.HasValue) return $"Unable to stage receipt: {error}";
            grn.GLBatchId = batchId; foreach (var movement in movements) movement.GLBatchId = batchId;
            if (!updatingReview) ctx.GoodsReceipts.Add(grn);
            foreach (var order in orders)
            {
                foreach (var line in grn.Lines.Where(x => x.PurchaseOrderLineId.HasValue && order.Lines.Any(o => o.Id == x.PurchaseOrderLineId)))
                {
                    var orderLine = order.Lines.Single(x => x.Id == line.PurchaseOrderLineId);
                    var received = await ctx.GoodsReceiptLines.Where(x => (!updatingReview || x.GoodsReceiptId != grn.Id) && x.PurchaseOrderLineId == orderLine.Id).SumAsync(x => x.QuantityReceived);
                    orderLine.QuantityReceived = received + line.QuantityReceived;
                }
                order.HasReceipt = true; order.IsFullyReceived = order.Lines.All(x => x.QuantityReceived + UomConversion.QuantityTolerance >= x.QuantityOrdered);
                if (order.Status is not (PurchaseOrderStatus.DraftInvoice or PurchaseOrderStatus.Invoiced or PurchaseOrderStatus.Closed)) order.Status = order.IsFullyReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
            }
            if (draft != null) ctx.GoodsReceiptDrafts.Remove(draft);
            await ctx.SaveChangesAsync(); return string.Empty;
        }

        public async Task<List<PurchaseOrder>> GetOpenPOsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId
                    && (p.Status == PurchaseOrderStatus.Open || p.Status == PurchaseOrderStatus.PartiallyReceived))
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();
        }
        public async Task<List<PurchaseOrder>> GetPOsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var orders = await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines).ThenInclude(l => l.PurchaseRequestLine).ThenInclude(l => l!.PurchaseRequest)
                .Where(p => p.CompanyId == companyId)
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();
            // Older returns reduced the cached order counter. Receipt records
            // retain the cumulative fulfilled quantity and do not reopen a line.
            var lineIds = orders.SelectMany(x => x.Lines).Select(x => x.Id).ToList();
            var received = await ctx.GoodsReceiptLines.AsNoTracking()
                .Where(x => x.PurchaseOrderLineId.HasValue && lineIds.Contains(x.PurchaseOrderLineId.Value))
                .GroupBy(x => x.PurchaseOrderLineId!.Value)
                .Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityReceived) })
                .ToDictionaryAsync(x => x.Id, x => x.Qty);
            foreach (var line in orders.SelectMany(x => x.Lines))
                line.QuantityReceived = Math.Max(line.QuantityReceived, received.GetValueOrDefault(line.Id));
            return orders;
        }
        public async Task<(Guid CurrencyId, string CurrencyCode, decimal Rate)> GetVendorCurrencyDataAsync(Guid vendorId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var vendor = await ctx.Vendors
                .Include(v => v.DefaultCurrency)
                .FirstOrDefaultAsync(v => v.Id == vendorId);

            if (vendor == null) return (Guid.Empty, "", 1);

            if (vendor.CurrencyId == Guid.Empty) return (Guid.Empty, "BASE", 1);

            var company = await ctx.CompanyDetails.FindAsync(companyId);
            if (company == null) return (Guid.Empty, "", 1);

            var latestRateEntry = await ctx.CurrencyManagements
                .Where(c => c.CompanyId == companyId && c.CurrencyId == vendor.CurrencyId)
                .OrderByDescending(c => c.Date)
                .FirstOrDefaultAsync();

            decimal rate = latestRateEntry?.Rate ?? 1.0m;

            return (vendor.CurrencyId, vendor.DefaultCurrency?.CurrencyCode ?? "???", rate);
        }
        public async Task<List<PurchaseOrder>> GetPOsReadyForInvoicingAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId
                         && p.HasReceipt == true
                         && p.IsInvoicePosted == false)
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();
        }
        public async Task<bool> CheckIfPoHasReceipts(Guid poId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.GoodsReceiptLines.AnyAsync(line => ctx.PurchaseOrderLines.Any(poLine =>
                poLine.Id == line.PurchaseOrderLineId && poLine.PurchaseOrderId == poId));
        }
        public async Task<string> UpdatePurchaseInvoiceDetailsAsync(Guid companyId, Guid invoiceId, string supplierInvoiceNumber, DateTime invoiceDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var invoice = await ctx.PurchaseInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId && x.CompanyId == companyId);
            if (invoice == null) return "Purchase invoice not found.";
            if (invoice.IsPosted) return "Posted purchase invoice details are locked.";
            var normalizedReference = PurchaseInvoiceReference.Resolve(invoice.PurchaseInvoiceNumber,
                supplierInvoiceNumber, invoice.SupplierInvoiceNumber, invoice.ExternalInvoiceNumber);
            if (string.IsNullOrWhiteSpace(normalizedReference)) return "This invoice has no document number. Check the purchase invoice numbering setup.";
            var referenceKey = normalizedReference.ToUpperInvariant();
            if (await ctx.PurchaseInvoices.AnyAsync(x => x.CompanyId == companyId && x.VendorId == invoice.VendorId
                && x.Id != invoiceId && ((x.SupplierInvoiceNumber != null && x.SupplierInvoiceNumber.ToUpper() == referenceKey)
                    || (x.ExternalInvoiceNumber != null && x.ExternalInvoiceNumber.ToUpper() == referenceKey))))
                return "That supplier invoice number is already registered for this supplier.";
            invoice.SupplierInvoiceNumber = normalizedReference;
            invoice.ExternalInvoiceNumber = normalizedReference;
            invoice.InvoiceDate = invoiceDate.Date;
            invoice.BillDate = invoiceDate.Date;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> PostPurchaseInvoiceAsync(Guid billId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var transaction = await ctx.Database.BeginTransactionAsync();
            try
            {

var stageError = await StagePurchaseInvoiceAsync(ctx, billId, userId);
            if (!string.IsNullOrEmpty(stageError)) return stageError;
            await ctx.SaveChangesAsync();
            await transaction.CommitAsync();
            return string.Empty;
            }
            catch (DbUpdateException) { return "The purchase invoice could not be submitted. Its draft and source reservations were retained."; }
            catch (Microsoft.Data.SqlClient.SqlException) { return "The purchase invoice could not be submitted because the database is busy. Try again."; }
            catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "The purchase invoice could not be submitted because another transaction is using it. Try again."; }
        }
    public async Task<string> StagePurchaseInvoiceAsync(AppDbContext ctx, Guid billId, string userId)
    {
            var bill = await ctx.PurchaseInvoices
                .FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {billId}")
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null) return "Bill not found.";
        var dimensionError = await DimensionService.ValidateAsync(ctx, bill.CompanyId, bill.Lines.Select(x => x.DimensionSetId), true);
        if (dimensionError.Length > 0) return dimensionError;
            if (!bill.IsDirectBill && bill.Lines.Any(x => x.GoodsReceiptLineId.HasValue) && bill.Lines.Any(x => !x.GoodsReceiptLineId.HasValue))
                return "Goods receipt lines and manual item lines cannot be combined on one invoice. Recreate this draft using one entry method.";
            var routeError = await InvoiceRoutePolicy.ValidateAsync(ctx, bill.CompanyId, TransactionTemplateModule.AP,
                new InvoiceRoute { TemplateId = bill.CustomTransactionTypeId, DebitAccountId = bill.OverrideExpenseGlAccountId, CreditAccountId = bill.OverrideAccountsPayableGlAccountId, TaxAccountId = bill.TaxGLAccountId, DiscountAccountId = bill.DiscountGlAccountId });
            if (routeError != null) return routeError;
            var customRoute = bill.CustomTransactionTypeId.HasValue ? await ctx.TransactionGlMappings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == bill.CompanyId && x.CustomTransactionTypeId == bill.CustomTransactionTypeId && x.IsActive) : null;
            var currencyError = await DocumentCurrencyPolicy.ValidateAsync(ctx, bill.CompanyId, bill.CurrencyId, bill.ExchangeRate);
            if (currencyError != null) return currencyError;
            var wasSubmitted = bill.IsPosted;
            if (bill.IsPosted)
            {
                var existingBatch = bill.GLBatchId.HasValue
                    ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bill.GLBatchId.Value && b.CompanyId == bill.CompanyId)
                    : null;
                if (existingBatch?.Status == BatchStatus.Posted)
                    return "STOP: This purchase invoice has already been committed to the General Ledger.";
                if (existingBatch?.Status == BatchStatus.Ready && bill.Lines.Any(x => x.GoodsReceiptLineId.HasValue))
                    return "STOP: This purchase invoice is already awaiting General Ledger review.";
                if (existingBatch == null)
                    return "STOP: This purchase invoice is already locked and has no review batch.";
            }

            var hasReceiptSources = bill.Lines.Any(x => x.GoodsReceiptLineId.HasValue);
            if (!bill.IsDirectBill)
            {
                var reference = PurchaseInvoiceReference.Resolve(bill.PurchaseInvoiceNumber,
                    bill.SupplierInvoiceNumber, existingExternalReference: bill.ExternalInvoiceNumber);
                if (string.IsNullOrWhiteSpace(reference)) return "This invoice has no document number. Check the purchase invoice numbering setup.";
                bill.SupplierInvoiceNumber = reference;
                bill.ExternalInvoiceNumber = reference;
            }
            if (bill.Lines.Count == 0 || bill.Lines.Any(x => x.QuantityBilled <= 0 || x.UnitCostBilled < 0)) return "STOP: Every invoice line needs a positive quantity and non-negative unit cost.";
            if (!bill.IsDirectBill && bill.Lines.Any(x => !x.GoodsReceiptLineId.HasValue && (!x.ItemId.HasValue || x.PurchaseOrderLineId.HasValue)))
                return "STOP: A manual invoice line must have an item and cannot claim a purchase order source.";
            if (!await ctx.Vendors.AnyAsync(x => x.Id == bill.VendorId && x.CompanyId == bill.CompanyId)
                || !await ctx.Currencies.AnyAsync(x => x.Id == bill.CurrencyId && x.CompanyId == bill.CompanyId))
                return "STOP: The invoice supplier or currency is unavailable in this company.";
            var invoiceItemIds = bill.Lines.Where(x => x.ItemId.HasValue).Select(x => x.ItemId!.Value).Distinct().ToList();
            var manualItems = await ctx.Items.Include(x => x.UomConversions).Where(x => x.CompanyId == bill.CompanyId && invoiceItemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (manualItems.Count != invoiceItemIds.Count) return "STOP: An invoice item is unavailable in this company.";
            var receiptSourceIds = bill.Lines.Where(x => x.GoodsReceiptLineId.HasValue)
                .Select(x => x.GoodsReceiptLineId!.Value).Distinct().ToList();
            var receiptLinesById = receiptSourceIds.Count == 0
                ? new Dictionary<Guid, GoodsReceiptLine>()
                : await ctx.GoodsReceiptLines.Where(x => receiptSourceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (receiptSourceIds.Count > 0 && receiptLinesById.Count != receiptSourceIds.Count)
                return "STOP: One or more source goods-receipt lines are missing.";
            if (receiptLinesById.Values.Any(x => x.PurchaseInvoiceLineId.HasValue)) return "STOP: Invoice-sourced receipts cannot be invoiced again.";
            var sourceReceiptIds = receiptLinesById.Values.Select(x => x.GoodsReceiptId).Distinct().ToList();
            var sourceReceipts = sourceReceiptIds.Count == 0
                ? new Dictionary<Guid, GoodsReceipt>()
                : await ctx.GoodsReceipts.Where(x => sourceReceiptIds.Contains(x.Id) && x.CompanyId == bill.CompanyId).ToDictionaryAsync(x => x.Id);
            if (receiptLinesById.Values.Any(x => !x.PurchaseOrderLineId.HasValue))
                return "STOP: A legacy goods receipt has an unresolved purchase order link. Restore its source before invoicing.";
            var sourcePoLineIds = receiptLinesById.Values.Select(x => x.PurchaseOrderLineId!.Value).Distinct().ToList();
            var sourcePoLines = sourcePoLineIds.Count == 0
                ? new Dictionary<Guid, PurchaseOrderLine>()
                : await ctx.PurchaseOrderLines.Where(x => sourcePoLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var sourcePoIds = sourcePoLines.Values.Select(x => x.PurchaseOrderId).Distinct().ToList();
            var sourceOrders = sourcePoIds.Count == 0
                ? new Dictionary<Guid, PurchaseOrder>()
                : await ctx.PurchaseOrders.Include(x => x.Lines)
                    .Where(x => sourcePoIds.Contains(x.Id) && x.CompanyId == bill.CompanyId).ToDictionaryAsync(x => x.Id);
            if (receiptSourceIds.Count > 0)
            {
                if (sourceReceipts.Count != sourceReceiptIds.Count || sourceOrders.Count != sourcePoIds.Count)
                    return "STOP: A source goods receipt or purchase order is unavailable in this company.";
                var batchIds = sourceReceipts.Values.Where(x => x.GLBatchId.HasValue).Select(x => x.GLBatchId!.Value).Distinct().ToList();
                var postedReceiptBatches = await ctx.GLBatches.Where(x => batchIds.Contains(x.Id)
                    && x.CompanyId == bill.CompanyId && x.Status == BatchStatus.Posted).Select(x => x.Id).ToListAsync();
                if (sourceReceipts.Values.Any(x => !x.GLBatchId.HasValue || !postedReceiptBatches.Contains(x.GLBatchId.Value)))
                    return "STOP: Every source goods receipt must be posted before its purchase invoice.";
                foreach (var line in bill.Lines.Where(x => x.GoodsReceiptLineId.HasValue))
                {
                    var receiptLine = receiptLinesById[line.GoodsReceiptLineId!.Value];
                    var receipt = sourceReceipts[receiptLine.GoodsReceiptId];
                    var poLine = sourcePoLines[receiptLine.PurchaseOrderLineId!.Value];
                    var order = sourceOrders[poLine.PurchaseOrderId];
                    if (receipt.CompanyId != bill.CompanyId || order.VendorId != bill.VendorId
                        || (bill.WarehouseId.HasValue && receipt.WarehouseId.HasValue && receipt.WarehouseId != bill.WarehouseId)
                        || order.CurrencyId != bill.CurrencyId)
                        return "STOP: Invoice supplier, site or currency does not match every source purchase order.";
                    if (line.PurchaseOrderLineId != poLine.Id || line.ItemId != poLine.ItemId || line.QuantityBilled < receiptLine.QuantityReceived
                        || line.UnitCostBilled != poLine.UnitCost || line.UomId != (receiptLine.UomId ?? poLine.UomId)
                        || line.UomConversionFactor != receiptLine.UomConversionFactor)
                        return "STOP: Invoice item, unit and cost must match the source, and its quantity must cover the received line.";
                }
                var priorBilled = await ctx.PurchaseInvoiceLines.Where(x => x.PurchaseInvoiceId != bill.Id && x.PurchaseOrderLineId.HasValue && sourcePoLineIds.Contains(x.PurchaseOrderLineId.Value))
                    .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityBilled) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
                foreach (var group in bill.Lines.Where(x => x.GoodsReceiptLineId.HasValue).GroupBy(x => x.PurchaseOrderLineId!.Value))
                {
                    var total = priorBilled.GetValueOrDefault(group.Key) + group.Sum(x => x.QuantityBilled);
                    var hasExtra = group.Any(x => x.QuantityBilled > receiptLinesById[x.GoodsReceiptLineId!.Value].QuantityReceived);
                    var ordered = sourcePoLines[group.Key].QuantityOrdered;
                    if (total > ordered || (hasExtra && total != ordered))
                        return "STOP: Invoice either the received quantity or the entire remaining order quantity for each source line.";
                }
            }

            // 1. Resolve Accounts Payable Liability via Mapping Router (Credit leg)
            Guid apAccount = bill.OverrideAccountsPayableGlAccountId ?? customRoute?.OverrideCreditGlAccountId ?? await _mappingService.GetMappedAccountAsync(
                bill.CompanyId,
                SystemTransactionType.PurchaseInvoice,
                isDebit: false,
                defaultAccountId: bill.AccountsPayableGlId);

            if (apAccount == Guid.Empty) return "STOP: Accounts Payable GL Account is not set or mapped.";

            // Validate Accounts in SegCOA
            bool apExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == apAccount && a.CompanyId == bill.CompanyId && a.IsActive);
            if (!apExists) return "STOP: Resolved AP Account ID is invalid or Inactive.";

            // Validate Period
            DateOnly postDate = DateOnly.FromDateTime(bill.BillDate);
            var period = await ctx.AccountingPeriods
                .FirstOrDefaultAsync(p => p.CompanyId == bill.CompanyId && p.StartDate <= postDate && p.EndDate >= postDate);

            if (period == null || period.IsClosed) return $"STOP: No Open Period for {postDate}.";

            var glLines = new List<GLJournalLine>();
            var vendorName = (await ctx.Vendors.FindAsync(bill.VendorId))?.Name ?? "Unknown";
            decimal totalDebitsBase = 0;

            // 2. Debits (Expense/Clearing Lines)
            foreach (var line in bill.Lines)
            {
                // Check line-level mapping override first, fallback to line default
                Guid expenseAccount;
                if (line.GoodsReceiptLineId.HasValue)
                {
                    var sourceReceiptLine = receiptLinesById[line.GoodsReceiptLineId.Value];
                    expenseAccount = sourceReceipts[sourceReceiptLine.GoodsReceiptId].InventoryGlAccountId;
                    if (expenseAccount == Guid.Empty || !await ctx.SegChartOfAccounts.AnyAsync(a =>
                            a.Id == expenseAccount && a.CompanyId == bill.CompanyId && a.IsActive))
                        return "STOP: A source goods receipt has no active GR/IR clearing account.";
                }
                else
                {
                    var item = line.ItemId.HasValue ? manualItems.GetValueOrDefault(line.ItemId.Value) : null;
                    expenseAccount = !bill.IsDirectBill && item != null && !item.IsService
                        ? await _mappingService.GetMappedAccountAsync(bill.CompanyId, SystemTransactionType.GoodsReceipt, isDebit: false,
                            defaultAccountId: bill.GoodsReceiptClearingGlAccountId ?? line.ExpenseGlAccountId)
                        : await _mappingService.GetMappedAccountAsync(bill.CompanyId, SystemTransactionType.PurchaseInvoice, isDebit: true,
                            defaultAccountId: line.ExpenseGlAccountId);
                }

                expenseAccount = bill.OverrideExpenseGlAccountId ?? customRoute?.OverrideDebitGlAccountId ?? expenseAccount;
                if (expenseAccount == Guid.Empty) return "STOP: Line missing Expense Account.";
                // Keep the posted clearing account so an invoice-first receipt
                // clears the same account even if configuration changes later.
                line.ExpenseGlAccountId = expenseAccount;

                decimal lineTotalBase = Math.Round(line.LineTotal * bill.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                totalDebitsBase += lineTotalBase;

                string glRef = !string.IsNullOrWhiteSpace(line.Description) ? line.Description : $"Purchase Invoice: {bill.ExternalInvoiceNumber}";

                glLines.Add(new GLJournalLine
                { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,
                    SegCoaId = expenseAccount,
                    Debit = lineTotalBase,
                    Credit = 0,
                    Reference = glRef
                });
            }

            // Apply the destination invoice's terms while clearing the full received cost.
            var grossForeign = bill.Lines.Sum(l => l.LineTotal);
            if (bill.ExchangeRate <= 0 || bill.DiscountPercentage < 0 || bill.DiscountPercentage > 100 || bill.DiscountAmount < 0
                || (bill.DiscountPercentage > 0 && bill.DiscountAmount > 0))
                return "STOP: Invalid exchange rate or discount terms.";
            var discountForeign = bill.DiscountPercentage > 0 ? grossForeign * bill.DiscountPercentage / 100m : Math.Min(bill.DiscountAmount, grossForeign);
            var netForeign = grossForeign - discountForeign;
            var discountBase = Math.Round(discountForeign * bill.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            if (discountBase > 0)
            {
                var discountAccount = bill.DiscountGlAccountId ?? await _mappingService.GetMappedAccountAsync(bill.CompanyId,
                    SystemTransactionType.DiscountReceived, isDebit: false, defaultAccountId: bill.DiscountGlAccountId ?? Guid.Empty);
                if (discountAccount == Guid.Empty || !await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == discountAccount && a.CompanyId == bill.CompanyId && a.IsActive))
                    return "STOP: An active Discount Received account is required.";
                glLines.Add(new GLJournalLine { SegCoaId = discountAccount, Credit = discountBase, Debit = 0, Reference = $"Discount {bill.PurchaseInvoiceNumber}" });
            }
            decimal taxForeign = 0;
            if (bill.TaxId.HasValue)
            {
                var tax = await ctx.Taxes.FirstOrDefaultAsync(t => t.Id == bill.TaxId.Value && t.CompanyId == bill.CompanyId);
                if (tax == null) return "STOP: The selected invoice tax is unavailable.";
                var taxAccount = bill.TaxGLAccountId ?? tax.GLAccountId;
                if (!taxAccount.HasValue || !await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == taxAccount.Value && a.CompanyId == bill.CompanyId && a.IsActive))
                    return "STOP: An active input tax account is required.";
                taxForeign = netForeign * tax.Per / 100m;
                var taxBase = Math.Round(taxForeign * bill.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                totalDebitsBase += taxBase;
                if (taxBase > 0) glLines.Add(new GLJournalLine { SegCoaId = taxAccount.Value, Debit = taxBase, Credit = 0, Reference = $"Input Tax: {bill.PurchaseInvoiceNumber}" });
            }
            if (bill.WithholdingTaxId.HasValue)
            {
                var withholding = await ctx.WithholdingTaxes.FirstOrDefaultAsync(w => w.Id == bill.WithholdingTaxId.Value && w.CompanyId == bill.CompanyId);
                if (withholding == null || withholding.WithholdingGlAccountId == Guid.Empty) return "STOP: The selected withholding tax is unavailable or has no GL account.";
                bill.WithholdingTaxName = withholding.Name;
                bill.WithholdingPercentage = withholding.PercentageValue;
                bill.WithholdingGlAccountId = withholding.WithholdingGlAccountId;
                bill.WithholdingAmountForeign = Math.Round(netForeign * withholding.PercentageValue / 100m, 4);
                bill.WithholdingAmount = Math.Round(bill.WithholdingAmountForeign * bill.ExchangeRate, 4);
            }
            else
            {
                bill.WithholdingTaxName = null; bill.WithholdingPercentage = 0; bill.WithholdingGlAccountId = null;
                bill.WithholdingAmountForeign = 0; bill.WithholdingAmount = 0;
            }
            // Withholding is settled at payment; the invoice records the full payable.
            bill.TotalAmountForeign = Math.Round(netForeign + taxForeign, 2, MidpointRounding.AwayFromZero);
            bill.TotalAmount = totalDebitsBase - discountBase;
            bill.AccountsPayableGlId = apAccount;
            glLines.Add(new GLJournalLine { SegCoaId = apAccount, Debit = 0, Credit = bill.TotalAmount,
                Reference = $"Purchase Invoice {bill.PurchaseInvoiceNumber} - {vendorName}" });

            // 5. Create and Post Journal Batch
            var (err, batchId) = bill.GLBatchId.HasValue
                ? await _glOps.RestageSubledgerBatchAsync(ctx, bill.CompanyId, bill.GLBatchId.Value, postDate, $"Purchase Invoice {bill.PurchaseInvoiceNumber}",
                    bill.PurchaseInvoiceNumber, glLines, userId, bill.ProjectId)
                : await _glOps.StageNewSubledgerBatchAsync(ctx, bill.CompanyId, postDate, "Purchase Invoice", $"Purchase Invoice {bill.PurchaseInvoiceNumber}",
                    bill.PurchaseInvoiceNumber, glLines, userId, inheritedProjectId: bill.ProjectId);

            if (!string.IsNullOrEmpty(err)) return $"GL ERROR: {err}";

            bill.GLBatchId = batchId;
            bill.IsPosted = true;
            bill.PostedDate = DateTime.Now;

            if (bill.PurchaseOrderId.HasValue)
            {
                var po = await ctx.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value);
                if (po != null) po.IsInvoicePosted = true;
            }

            if (receiptSourceIds.Count > 0 && !wasSubmitted)
            {
                var billedLines = bill.Lines.Where(x => x.GoodsReceiptLineId.HasValue).Select(x => sourcePoLines[receiptLinesById[x.GoodsReceiptLineId!.Value].PurchaseOrderLineId!.Value])
                    .DistinctBy(x => x.Id).ToList();
                foreach (var poLine in billedLines)
                {
                    var billedQuantity = bill.Lines.Where(x => x.PurchaseOrderLineId == poLine.Id).Sum(x => x.QuantityBilled);
                    poLine.QuantityBilled += billedQuantity;
                }
                foreach (var order in sourceOrders.Values)
                {
                    order.IsInvoicePosted = order.Lines.All(x => x.QuantityBilled >= x.QuantityReceived);
                    if (order.IsFullyReceived && order.IsInvoicePosted) order.Status = PurchaseOrderStatus.Invoiced;
                }
            }


        return string.Empty;
    }

        public async Task<string> PostVendorPaymentAsync(VendorPayment payment, Guid companyId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(payment.Reference))
                    payment.Reference = await _numberingService.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AP_VendorPayment, payment.Date);

                var existingPayment = payment.Id != Guid.Empty
                    ? await ctx.Set<VendorPayment>().FirstOrDefaultAsync(p => p.Id == payment.Id)
                    : null;
                var existingPaymentBatch = existingPayment?.GLBatchId.HasValue == true
                    ? await ctx.GLBatches.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == existingPayment.GLBatchId!.Value && b.CompanyId == companyId)
                    : null;

                if (existingPaymentBatch?.Status == BatchStatus.Posted)
                    return "This vendor payment has already been committed to the General Ledger and cannot be modified.";
                if (existingPaymentBatch?.Status == BatchStatus.Ready)
                    return "This vendor payment is already awaiting General Ledger review.";
                if (existingPayment != null && existingPaymentBatch == null)
                    return "Only draft payments or payments with an active review batch can be edited.";

                var bill = await ctx.PurchaseInvoices
                    .FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {payment.PurchaseInvoiceId}")
                    .Include(b => b.Payments)
                    .FirstOrDefaultAsync(b => b.Id == payment.PurchaseInvoiceId && b.CompanyId == companyId);

                if (bill == null) return "Vendor Bill not found.";
                if (!(bill.PurchaseInvoiceNumber.StartsWith("PINV-LEGACY-", StringComparison.Ordinal) && bill.IsPosted && !bill.GLBatchId.HasValue)
                    && !await NewInvoiceCorrectionService.IsPostedPurchaseAsync(ctx, bill)) return "Cannot pay an invoice until its ledger batch is posted.";
                if (payment.Amount <= 0) return "Payment amount must be greater than zero.";
                payment.ProjectId = bill.ProjectId;

                decimal rate = bill.ExchangeRate > 0 ? bill.ExchangeRate : 1.0m;

                // 1. Genuine Cash Payments already paid
                var postedBatchIds = await ctx.GLBatches.AsNoTracking()
                    .Where(b => b.CompanyId == companyId && (b.Status == BatchStatus.Posted || b.Status == BatchStatus.Ready))
                    .Select(b => b.Id)
                    .ToListAsync();
                decimal grossPaidBase = bill.Payments
                    .Where(p => p.Id != payment.Id && p.Amount > 0
                        && (!p.GLBatchId.HasValue || postedBatchIds.Contains(p.GLBatchId.Value)))
                    .Sum(p => p.Amount + p.WithholdingAmount);

                // 2. Query Debit Notes directly from DebitNotes table using the Invoice ID (PurchaseOrderId)
                decimal debitNotesBase = await InvoiceFinancialBalances.PurchaseDebitBaseAsync(ctx, bill, includeReserved: true);
                var paidBack = await ctx.VendorReturns.Where(x => x.PurchaseInvoiceId == bill.Id && x.Status == VendorReturnStatus.Posted
                    && (x.ReturnType == VendorReturnType.PaymentOnly || x.ReturnType == VendorReturnType.Both)
                    && (!x.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == x.GlBatchId.Value && b.Status == BatchStatus.Posted)))
                    .SumAsync(x => x.TotalAmount);
                grossPaidBase = Math.Max(0, grossPaidBase - Math.Round(paidBack * rate, 2));

                // 3. Net Invoice Liability = Bill Total - Debit Notes
                decimal netBillTotalBase = Math.Max(0, bill.TotalAmount - debitNotesBase);

                // 4. Remaining AP liability includes both bank cash and withholding settlements.
                decimal remainingBalanceBase = Math.Max(0, netBillTotalBase - grossPaidBase);

                decimal withholdingRatio = bill.TotalAmount > 0 && bill.WithholdingAmount > 0
                    ? Math.Min(0.999999m, bill.WithholdingAmount / bill.TotalAmount)
                    : 0m;
                decimal remainingWithholdingBase = Math.Min(
                    Math.Max(0, bill.WithholdingAmount - bill.Payments
                        .Where(p => p.Id != payment.Id && p.Amount > 0 && (!p.GLBatchId.HasValue || postedBatchIds.Contains(p.GLBatchId.Value)))
                        .Sum(p => p.WithholdingAmount)),
                    remainingBalanceBase * withholdingRatio);
                decimal remainingBankBase = Math.Max(0, remainingBalanceBase - remainingWithholdingBase);

                if (payment.Amount > remainingBankBase + 0.01m)
                {
                    decimal remainingForeign = Math.Round(remainingBankBase / rate, 2);
                    decimal paymentForeign = Math.Round(payment.Amount / rate, 2);
                    return $"Payment of {paymentForeign:N2} exceeds remaining payable balance of {remainingForeign:N2} (Invoice reduced by Debit Notes).";
                }

                payment.WithholdingAmount = Math.Round(payment.Amount <= 0 || withholdingRatio <= 0
                    ? 0
                    : payment.Amount * withholdingRatio / (1m - withholdingRatio), 4);
                payment.WithholdingAmount = Math.Min(payment.WithholdingAmount, remainingWithholdingBase);
                payment.WithholdingAmountForeign = Math.Round(payment.WithholdingAmount / rate, 4);
                payment.WithholdingTaxId = bill.WithholdingTaxId;
                payment.WithholdingTaxName = bill.WithholdingTaxName;
                payment.WithholdingPercentage = bill.WithholdingPercentage;
                payment.WithholdingGlAccountId = bill.WithholdingGlAccountId;
                if (payment.WithholdingAmount > 0 && payment.WithholdingGlAccountId == null)
                    return "Withholding tax GL account is missing on the invoice setup.";

                TransactionGlMapping? customMapping = null;
                if (payment.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.CustomTransactionTypeId == payment.CustomTransactionTypeId.Value);
                }

                // Debit: AP
                Guid debitApAccountId = payment.OverrideDebitApGlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? await _mappingService.GetMappedAccountAsync(
                        companyId,
                        SystemTransactionType.VendorPayment,
                        isDebit: true,
                        defaultAccountId: bill.AccountsPayableGlId);

                // Credit: Bank / Cash
                Guid creditBankAccountId = payment.OverrideCreditBankGlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? (payment.BankGlAccountId != Guid.Empty ? payment.BankGlAccountId : Guid.Empty);

                if (creditBankAccountId == Guid.Empty)
                {
                    creditBankAccountId = await _mappingService.GetMappedAccountAsync(
                        companyId,
                        SystemTransactionType.VendorPayment,
                        isDebit: false,
                        defaultAccountId: Guid.Empty);
                }

                if (debitApAccountId == Guid.Empty) return "Configuration Error: Missing Accounts Payable Account for payment debit.";
                if (creditBankAccountId == Guid.Empty) return "Configuration Error: Missing Bank / Cash GL Account for payment credit.";

                payment.BankGlAccountId = creditBankAccountId;
                if (payment.Id == Guid.Empty) payment.Id = Guid.NewGuid();

                if (payment.WithholdingAmount > 0)
                {
                    bool withholdingAccountIsValid = payment.WithholdingGlAccountId.HasValue
                        && await ctx.SegChartOfAccounts.AnyAsync(a =>
                            a.Id == payment.WithholdingGlAccountId.Value
                            && a.CompanyId == companyId
                            && a.IsActive);

                    if (!withholdingAccountIsValid)
                        return "Configuration Error: The withholding tax GL account is missing or inactive.";
                }

                var glLines = new List<GLJournalLine>
        {
            new GLJournalLine
            {
                SegCoaId = debitApAccountId,
                Debit = payment.Amount + payment.WithholdingAmount,
                Credit = 0,
                Reference = $"Pay Bill: {bill.ExternalInvoiceNumber}"
            },
            new GLJournalLine
            {
                SegCoaId = creditBankAccountId,
                Debit = 0,
                Credit = payment.Amount,
                Reference = $"Disbursement: {bill.ExternalInvoiceNumber}"
            }
        };
                if (payment.WithholdingAmount > 0)
                {
                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = payment.WithholdingGlAccountId!.Value,
                        Debit = 0,
                        Credit = payment.WithholdingAmount,
                        Reference = $"Withholding Tax: {bill.ExternalInvoiceNumber}"
                    });
                }

                var paymentDimensions = await ctx.PurchaseInvoiceLines.AsNoTracking().Where(x => x.PurchaseInvoiceId == bill.Id)
                    .Select(x => new { x.DimensionSetId, x.QuantityBilled, x.UnitCostBilled }).ToListAsync();
                var paymentWeights = await DimensionService.PostedInvoiceWeightsAsync(ctx, companyId, bill.GLBatchId, false,
                    "Purchase Invoice " + bill.PurchaseInvoiceNumber + " - ", paymentDimensions.Select(x => (x.DimensionSetId, Math.Abs(x.QuantityBilled * x.UnitCostBilled))));
                DimensionService.AllocateSummaryLines(glLines, payment.DimensionSetId.HasValue
                    ? new[] { (payment.DimensionSetId, 1m) } : paymentWeights);

                decimal totalDebits = Math.Round(glLines.Sum(l => l.Debit), 4, MidpointRounding.AwayFromZero);
                decimal totalCredits = Math.Round(glLines.Sum(l => l.Credit), 4, MidpointRounding.AwayFromZero);
                if (totalDebits != totalCredits)
                    return $"Payment posting is not balanced. Debits: {totalDebits:N4}; Credits: {totalCredits:N4}.";

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    companyId,
                    DateOnly.FromDateTime(payment.Date),
                    "Vendor Payment",
                    string.IsNullOrWhiteSpace(payment.Reference) ? $"Pay {bill.ExternalInvoiceNumber}" : payment.Reference,
                    glLines,
                    userId,
                    existingBatchId: payment.GLBatchId,
                    inheritedProjectId: bill.ProjectId);

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                payment.GLBatchId = batchId;

                if (existingPayment == null)
                {
                    ctx.Set<VendorPayment>().Add(payment);
                }
                else
                {
                    ctx.Entry(existingPayment).CurrentValues.SetValues(payment);
                }

                var stagedBatch = batchId.HasValue
                    ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId.Value && b.CompanyId == companyId)
                    : null;
                if (bill.PurchaseOrderId.HasValue && stagedBatch?.Status == BatchStatus.Posted
                    && (grossPaidBase + payment.Amount + payment.WithholdingAmount) >= netBillTotalBase - 0.01m)
                {
                    var po = await ctx.PurchaseOrders.FindAsync(bill.PurchaseOrderId.Value);
                    if (po != null)
                    {
                        po.IsFullyPaid = true;
                        po.Status = PurchaseOrderStatus.Closed;
                    }
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Payment Error: {ex.Message}";
            }
        }
        public async Task<string> DeletePurchaseOrderAsync(Guid poId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var transaction = await ctx.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == poId);

            if (po == null) return "Purchase Order not found.";

            // SECURITY PRECAUTION: Prevent deleting POs with financial/inventory impact
            if (po.HasReceipt || po.IsInvoicePosted)
            {
                return "STOP: Cannot delete a Purchase Order that has already received goods or been billed. It must be kept for auditing.";
            }
            var poLineIds = po.Lines.Select(x => x.Id).ToList();
            if (await ctx.GoodsReceiptDraftLines.AnyAsync(x => x.PurchaseOrderLineId.HasValue && poLineIds.Contains(x.PurchaseOrderLineId.Value)))
                return "This order has quantities reserved by a goods receipt draft. Delete the draft before deleting the order.";

            try
            {
                // Remove the child line items first, then the header
                ctx.PurchaseOrderLines.RemoveRange(po.Lines);
                ctx.PurchaseOrders.Remove(po);

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"DATABASE ERROR: {ex.Message}";
            }
        }

        // ==========================================
        // DIRECT AP BILL (NON-PO / EXPENSE)
        // ==========================================
        public async Task<string> SaveDirectPurchaseInvoiceDraftAsync(PurchaseInvoice draft)
        {
            if (draft.CompanyId == Guid.Empty || draft.VendorId == Guid.Empty || draft.CurrencyId == Guid.Empty) return "Select a supplier and currency.";
            if (draft.Lines.Count == 0 || draft.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.QuantityBilled <= 0 || x.UnitCostBilled < 0)) return "Every direct invoice line needs a description, positive quantity and non-negative cost.";
            await using var db = await _dbFactory.CreateDbContextAsync();
            var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == draft.VendorId && x.CompanyId == draft.CompanyId);
            if (vendor == null) return "The selected supplier is unavailable.";
            var currencyError = await DocumentCurrencyPolicy.ValidateAsync(db, draft.CompanyId, draft.CurrencyId, draft.ExchangeRate, vendor.CurrencyId);
            if (currencyError != null) return currencyError;
            var routeError = await InvoiceRoutePolicy.ValidateAsync(db, draft.CompanyId, TransactionTemplateModule.AP, new InvoiceRoute {
                TemplateId = draft.CustomTransactionTypeId, DebitAccountId = draft.OverrideExpenseGlAccountId, CreditAccountId = draft.OverrideAccountsPayableGlAccountId, TaxAccountId = draft.TaxGLAccountId, DiscountAccountId = draft.DiscountGlAccountId });
            if (routeError != null) return routeError;
            var number = draft.PurchaseInvoiceNumber;
            if (string.IsNullOrEmpty(number)) number = await _numberingService.GenerateAndReserveNextNumberAsync(draft.CompanyId, SequenceTargetType.AP_Invoice, draft.BillDate);
            await using var tx = await db.Database.BeginTransactionAsync();
            var existing = await db.PurchaseInvoices
                .FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {draft.Id} AND [CompanyId] = {draft.CompanyId}")
                .Include(x => x.Lines).FirstOrDefaultAsync();
            if (existing != null && (!existing.IsDirectBill || existing.IsPosted || existing.GLBatchId.HasValue)) return "Only an unsubmitted direct invoice draft can be saved here.";
            if (draft.DiscountPercentage < 0 || draft.DiscountPercentage > 100 || draft.DiscountAmount < 0 || (draft.DiscountPercentage > 0 && draft.DiscountAmount > 0)) return "Enter valid discount terms.";
            var gross = draft.Lines.Sum(x => x.LineTotal);
            var discount = draft.DiscountPercentage > 0 ? gross * draft.DiscountPercentage / 100m : Math.Min(gross, draft.DiscountAmount);
            var tax = draft.TaxId.HasValue ? await db.Taxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == draft.TaxId && x.CompanyId == draft.CompanyId) : null;
            if (draft.TaxId.HasValue && tax == null) return "The selected tax is unavailable.";
            draft.IsDirectBill = true; draft.IsPosted = false; draft.GLBatchId = null;
            draft.PurchaseInvoiceNumber = existing?.PurchaseInvoiceNumber ?? number;
            draft.InvoiceDate = draft.BillDate; draft.ExternalInvoiceNumber ??= ""; draft.MatchVarianceReason ??= "";
            draft.TotalAmountForeign = Math.Round((gross - discount) * (1 + (tax?.Per ?? 0) / 100m), 2);
            draft.TotalAmount = Math.Round(draft.TotalAmountForeign * draft.ExchangeRate, 2);
            if (existing == null) db.PurchaseInvoices.Add(draft);
            else {
                db.PurchaseInvoiceLines.RemoveRange(existing.Lines); existing.Lines.Clear();
                db.Entry(existing).CurrentValues.SetValues(draft);
                foreach (var row in draft.Lines) { row.Id = Guid.NewGuid(); row.PurchaseInvoiceId = existing.Id; row.PurchaseInvoice = existing; db.PurchaseInvoiceLines.Add(row); }
            }
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }

        public async Task<string> PostDirectBillAsync(PurchaseInvoice bill, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                if (bill.CompanyId == Guid.Empty) return "Company ID is missing.";
                if (bill.VendorId == Guid.Empty) return "Vendor is required.";
                if (bill.CurrencyId == Guid.Empty) return "Currency is required.";
                if (!bill.Lines.Any()) return "Bill must have at least one line.";
                if (bill.Lines.Any(line => string.IsNullOrWhiteSpace(line.Description) || line.QuantityBilled <= 0
                    || line.UnitCostBilled < 0))
                    return "Every direct purchase invoice line needs a description, positive quantity and non-negative unit cost.";

                var routeError = await InvoiceRoutePolicy.ValidateAsync(ctx, bill.CompanyId, TransactionTemplateModule.AP, new InvoiceRoute {
                    TemplateId = bill.CustomTransactionTypeId, DebitAccountId = bill.OverrideExpenseGlAccountId, CreditAccountId = bill.OverrideAccountsPayableGlAccountId, TaxAccountId = bill.TaxGLAccountId, DiscountAccountId = bill.DiscountGlAccountId });
                if (routeError != null) return routeError;
                var vendor = await ctx.Vendors.FindAsync(bill.VendorId);
                if (vendor == null || vendor.CompanyId != bill.CompanyId) return "Selected vendor is not available in this company.";
                var directCurrency = await ctx.PurchaseInvoices.Where(x => x.Id == bill.Id && x.CompanyId == bill.CompanyId && x.VendorId == bill.VendorId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync() ?? vendor.CurrencyId;
                var directCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(ctx, bill.CompanyId, bill.CurrencyId, bill.ExchangeRate, directCurrency);
                if (directCurrencyError != null) return directCurrencyError;
                if (!await ctx.Currencies.AnyAsync(x => x.Id == bill.CurrencyId && x.CompanyId == bill.CompanyId))
                    return "Selected currency is not available in this company.";
                if (bill.WarehouseId.HasValue && !await ctx.Warehouses.AnyAsync(x => x.Id == bill.WarehouseId.Value && x.CompanyId == bill.CompanyId))
                    return "Selected site is not available in this company.";
                if (bill.DiscountPercentage < 0 || bill.DiscountPercentage > 100 || bill.DiscountAmount < 0)
                    return "Purchase invoice discount values are invalid.";

                if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();
                if (string.IsNullOrWhiteSpace(bill.PurchaseInvoiceNumber))
                    bill.PurchaseInvoiceNumber = await _numberingService.GenerateAndReserveNextNumberAsync(
                        bill.CompanyId, SequenceTargetType.AP_Invoice, bill.BillDate);
                if (await ctx.PurchaseInvoices.AnyAsync(x => x.CompanyId == bill.CompanyId
                    && x.PurchaseInvoiceNumber == bill.PurchaseInvoiceNumber && x.Id != bill.Id))
                    return $"Purchase invoice number '{bill.PurchaseInvoiceNumber}' is already in use.";

                var existingBill = bill.Id != Guid.Empty
                    ? await ctx.PurchaseInvoices
                        .FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {bill.Id} AND [CompanyId] = {bill.CompanyId}")
                        .Include(b => b.Lines).FirstOrDefaultAsync()
                    : null;
                var existingBillBatch = existingBill?.GLBatchId.HasValue == true
                    ? await ctx.GLBatches.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == existingBill.GLBatchId!.Value && b.CompanyId == bill.CompanyId)
                    : null;

                if (existingBillBatch?.Status == BatchStatus.Posted)
                    return "This direct bill has already been committed to the General Ledger and cannot be modified.";
                if (existingBill != null && existingBill.IsPosted && existingBillBatch == null)
                    return "This direct bill has already been committed and cannot be modified.";

                if (string.IsNullOrWhiteSpace(bill.ExternalInvoiceNumber))
                    bill.ExternalInvoiceNumber = $"INV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";

                // 1. Resolve custom template mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (bill.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == bill.CompanyId && m.CustomTransactionTypeId == bill.CustomTransactionTypeId.Value);
                }

                // 2. Resolve Expense Account (Debit)
                Guid defaultExpenseAccount = bill.OverrideExpenseGlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? await _mappingService.GetMappedAccountAsync(
                        bill.CompanyId,
                        SystemTransactionType.DirectPurchaseInvoice,
                        isDebit: true,
                        defaultAccountId: Guid.Empty);

                if (defaultExpenseAccount == Guid.Empty)
                    return "An Expense GL Account is required for Direct Bills. Please configure it in GL Mapping Settings or in the Route Modal.";

                // 3. Resolve Accounts Payable Account (Credit)
                Guid apAccount = bill.OverrideAccountsPayableGlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? await _mappingService.GetMappedAccountAsync(
                        bill.CompanyId,
                        SystemTransactionType.DirectPurchaseInvoice,
                        isDebit: false,
                        defaultAccountId: vendor.PayablesAccountId ?? bill.AccountsPayableGlId);

                if (apAccount == Guid.Empty)
                    return "Accounts Payable Liability Account is required. Please check Vendor setup or GL Mapping settings.";

                bill.AccountsPayableGlId = apAccount;

                decimal rate = bill.ExchangeRate > 0 ? bill.ExchangeRate : 1;
                decimal totalGrossForeign = 0;
                decimal grossBaseForLedger = 0;
                var glLines = new List<GLJournalLine>();

                // Process Lines (Debit Expenses)
                foreach (var line in bill.Lines)
                {
                    Guid lineExpenseAccount = bill.OverrideExpenseGlAccountId ?? customMapping?.OverrideDebitGlAccountId ?? (line.ExpenseGlAccountId != Guid.Empty
                        ? line.ExpenseGlAccountId
                        : defaultExpenseAccount);

                    line.ExpenseGlAccountId = lineExpenseAccount;

                    decimal lineTotalForeign = line.LineTotal;
                    totalGrossForeign += lineTotalForeign;

                    decimal lineTotalBase = Math.Round(lineTotalForeign * rate, 2);
                    grossBaseForLedger += lineTotalBase;

                    string glRef = !string.IsNullOrWhiteSpace(line.Description)
                        ? line.Description
                        : $"Direct Bill: {bill.ExternalInvoiceNumber}";

                    glLines.Add(new GLJournalLine
                    { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,
                        SegCoaId = lineExpenseAccount,
                        Debit = lineTotalBase,
                        Credit = 0,
                        Reference = glRef
                    });
                }

                // Process Tax (Debit Tax Asset / Input VAT)
                var discountForeign = bill.DiscountPercentage > 0 ? totalGrossForeign * bill.DiscountPercentage / 100m : Math.Min(totalGrossForeign, bill.DiscountAmount);
                var netForeign = totalGrossForeign - discountForeign;
                var discountBase = Math.Round(discountForeign * rate, 2, MidpointRounding.AwayFromZero);
                if (discountBase > 0) {
                    var account = bill.DiscountGlAccountId ?? await _mappingService.GetMappedAccountAsync(bill.CompanyId, SystemTransactionType.DiscountReceived, false, Guid.Empty);
                    if (account == Guid.Empty) return "Configure a Discount Received account.";
                    glLines.Add(new GLJournalLine { SegCoaId = account, Credit = discountBase, Reference = $"Discount {bill.PurchaseInvoiceNumber}" });
                }
                decimal taxForeign = 0;
                decimal taxBaseForLedger = 0;

                if (bill.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FirstOrDefaultAsync(x => x.Id == bill.TaxId && x.CompanyId == bill.CompanyId);
                    bill.TaxGLAccountId ??= tax?.GLAccountId;
                    if (tax == null || !bill.TaxGLAccountId.HasValue) return "Configure an input tax account.";
                    if (tax != null)
                    {
                        taxForeign = netForeign * (tax.Per / 100);
                        taxBaseForLedger = Math.Round(taxForeign * rate, 2);

                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = bill.TaxGLAccountId.Value,
                            Debit = taxBaseForLedger,
                            Credit = 0,
                            Reference = $"Input Tax: {bill.ExternalInvoiceNumber}"
                        });
                    }
                }

                // Credit Accounts Payable Liability
                bill.TotalAmountForeign = netForeign + taxForeign;
                decimal actualCreditBase = grossBaseForLedger - discountBase + taxBaseForLedger;
                bill.TotalAmount = actualCreditBase;

                if (bill.WithholdingTaxId.HasValue)
                {
                    var withholding = await ctx.WithholdingTaxes.AsNoTracking()
                        .FirstOrDefaultAsync(w => w.Id == bill.WithholdingTaxId.Value && w.CompanyId == bill.CompanyId);
                    if (withholding == null) return "Selected withholding tax is not available for this company.";
                    if (withholding.WithholdingGlAccountId == Guid.Empty) return "Selected withholding tax has no GL account mapped.";
                    bill.WithholdingTaxName = withholding.Name;
                    bill.WithholdingPercentage = Math.Round(withholding.PercentageValue, 4);
                    bill.WithholdingGlAccountId = withholding.WithholdingGlAccountId;
                    bill.WithholdingAmountForeign = Math.Round(netForeign * withholding.PercentageValue / 100m, 4);
                    bill.WithholdingAmount = Math.Round(bill.WithholdingAmountForeign * rate, 4);
                }
                else
                {
                    bill.WithholdingTaxName = null;
                    bill.WithholdingPercentage = 0;
                    bill.WithholdingAmountForeign = 0;
                    bill.WithholdingAmount = 0;
                    bill.WithholdingGlAccountId = null;
                }

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = apAccount,
                    Debit = 0,
                    Credit = actualCreditBase,
                    Reference = $"Vendor Bill: {bill.ExternalInvoiceNumber}"
                });

                if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();
                bill.IsDirectBill = true;
                bill.IsPosted = true;
                bill.PostedDate = DateTime.Now;
                bill.MatchStatus = BillMatchStatus.NoPoLinked;

                foreach (var line in bill.Lines)
                {
                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                    line.PurchaseInvoiceId = bill.Id;
                }

                var (err, batchId) = bill.GLBatchId.HasValue
                    ? await _glOps.RestageSubledgerBatchAsync(ctx, bill.CompanyId, bill.GLBatchId.Value, DateOnly.FromDateTime(bill.BillDate), $"Bill {bill.PurchaseInvoiceNumber}", bill.PurchaseInvoiceNumber, glLines, userId, bill.ProjectId)
                    : await _glOps.StageNewSubledgerBatchAsync(ctx, bill.CompanyId, DateOnly.FromDateTime(bill.BillDate), $"Direct Bill {bill.PurchaseInvoiceNumber}", $"Bill {bill.PurchaseInvoiceNumber}", bill.PurchaseInvoiceNumber, glLines, userId, bill.ProjectId);
                if (!string.IsNullOrEmpty(err)) return err;
                bill.GLBatchId = batchId;

                if (existingBill == null)
                {
                    ctx.PurchaseInvoices.Add(bill);
                    ctx.PurchaseInvoiceLines.AddRange(bill.Lines);
                }
                else
                {
                    bill.CompanyId = existingBill.CompanyId;
                    ctx.Entry(existingBill).CurrentValues.SetValues(bill);
                    ctx.PurchaseInvoiceLines.RemoveRange(existingBill.Lines);
                    existingBill.Lines.Clear();
                    foreach (var line in bill.Lines)
                    {
                        line.Id = Guid.NewGuid();
                        line.PurchaseInvoiceId = existingBill.Id;
                        line.PurchaseInvoice = existingBill;
                        ctx.PurchaseInvoiceLines.Add(line);
                    }
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Direct Bill Error: {ex.Message}";
            }
        }
        public async Task<List<PurchaseInvoice>> GetDirectBillsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.PurchaseInvoices
                .AsNoTracking()
                .Include(b => b.Lines)
                .Include(b => b.Payments)
                .Where(b => b.CompanyId == companyId && b.IsDirectBill == true)
                .OrderByDescending(b => b.BillDate)
                .ToListAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // 1. PO AGING REPORT (INVOICED ONLY)
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GeneratePOAgingReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            // Fetch base open/active POs within range
            var pos = await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId
                         && p.Status != PurchaseOrderStatus.Closed
                         && p.OrderDate >= startDt && p.OrderDate <= endDt)
                .OrderBy(p => p.OrderDate)
                .ToListAsync();

            var poNumbers = pos.Select(p => p.OrderNumber).ToList();

            // Locate downstream invoices tied to these PO numbers
            var invoices = await ctx.PurchaseOrders
                .AsNoTracking()
                .Where(i => i.CompanyId == companyId
                         && !string.IsNullOrEmpty(i.ConvertedFromPONumber)
                         && poNumbers.Contains(i.ConvertedFromPONumber))
                .ToDictionaryAsync(i => i.ConvertedFromPONumber!, i => i.OrderNumber);

            // FILTER: Keep only POs that possess an invoice number link
            var invoicedPOs = pos.Where(p => invoices.ContainsKey(p.OrderNumber)).ToList();

            var vendorIds = invoicedPOs.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var today = DateOnly.FromDateTime(DateTime.Today);

            var reportData = new StandardReportData
            {
                ReportName = "Purchase Order Aging Report (Invoiced Orders)",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Invoice Order Number", "Vendor", "Order Date",
                    "Days Outstanding", "Order Value", "Status", "Aging Bucket"
                },
                Rows = new List<List<string>>()
            };

            decimal grandTotal = 0;

            foreach (var po in invoicedPOs)
            {
                string invoiceNumber = invoices[po.OrderNumber]; // Guaranteed to exist via filter
                decimal orderValue = po.Lines.Sum(l => l.LineTotal) * po.ExchangeRate;
                int days = today.DayNumber - DateOnly.FromDateTime(po.OrderDate.Date).DayNumber;

                string bucket = days <= 30 ? "Current (0–30 Days)"
                              : days <= 60 ? "31–60 Days"
                              : days <= 90 ? "61–90 Days"
                              : "Over 90 Days";

                reportData.Rows.Add(new List<string>
                {
                    invoiceNumber, // Replaced PO Number with Invoice Order Number
                    vendors.GetValueOrDefault(po.VendorId, "Unknown"),
                    po.OrderDate.ToString("MMM dd, yyyy"),
                    days.ToString("N0"),
                    orderValue.ToString("N2"),
                    po.Status.ToString(),
                    bucket
                });

                grandTotal += orderValue;
            }

            reportData.Rows.Add(new List<string>
                { "", "GRAND TOTAL", "", "", grandTotal.ToString("N2"), "", "" });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. GOODS RECEIPT NOTE (GRN) LOG
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GenerateGRNLogReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            var grns = await ctx.GoodsReceipts
                .AsNoTracking()
                .Include(g => g.Lines)
                .Where(g => g.CompanyId == companyId
                         && g.DateReceived >= startDt && g.DateReceived <= endDt)
                .OrderByDescending(g => g.DateReceived)
                .ToListAsync();

            var poLineIds = grns.SelectMany(g => g.Lines).Where(l => l.PurchaseOrderLineId.HasValue).Select(l => l.PurchaseOrderLineId!.Value).Distinct().ToList();
            var poLineDict = await ctx.PurchaseOrderLines.AsNoTracking()
                .Where(line => poLineIds.Contains(line.Id)).ToDictionaryAsync(line => line.Id);
            var poIds = poLineDict.Values.Select(line => line.PurchaseOrderId).Distinct().ToList();
            var poDict = await ctx.PurchaseOrders.AsNoTracking()
                .Where(order => poIds.Contains(order.Id) && order.CompanyId == companyId)
                .ToDictionaryAsync(order => order.Id);

            var itemIds = poLineDict.Values
                .Select(line => line.ItemId)
                .Distinct()
                .ToList();

            var items = await ctx.Items
                .AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            var vendorIds = poDict.Values.Select(order => order.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .AsNoTracking()
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var batchIds = grns
                .Where(g => g.GLBatchId.HasValue)
                .Select(g => g.GLBatchId!.Value)
                .Distinct()
                .ToList();
            var batchStatuses = await ctx.GLBatches
                .AsNoTracking()
                .Where(b => b.CompanyId == companyId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Status);

            var reportData = new StandardReportData
            {
                ReportName = "Goods Receipt Note (GRN) Log",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "GRN Number", "Invoice Number", "Vendor", "Date Received",
                    "Item", "Transaction UOM", "Qty Received (UOM)", "Qty Received (Primary)",
                    "Unit Cost (Transaction UOM)", "Line Value", "GL Status"
                },
                Rows = new List<List<string>>()
            };

            decimal grandValue = 0;
            decimal grandQtyPrimary = 0;
            bool hasUnresolvedValue = false;

            foreach (var grn in grns)
            {
                string glStatus = grn.GLBatchId.HasValue && batchStatuses.TryGetValue(grn.GLBatchId.Value, out var status)
                    ? status.ToString()
                    : "Not posted";

                foreach (var line in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    var poLine = line.PurchaseOrderLineId.HasValue ? poLineDict.GetValueOrDefault(line.PurchaseOrderLineId.Value) : null;
                    var po = poLine != null ? poDict.GetValueOrDefault(poLine.PurchaseOrderId) : null;
                    if (poLine == null || po == null)
                    {
                        var legacyFactor = UomConversion.NormalizeFactor(line.UomConversionFactor);
                        reportData.Rows.Add(new List<string>
                        {
                            grn.GrnNumber, "Unresolved PO line: " + (line.LegacyPurchaseOrderLineId?.ToString() ?? "unknown"),
                            "Unknown", grn.DateReceived.ToString("MMM dd, yyyy"), "Unknown (legacy receipt)",
                            string.IsNullOrWhiteSpace(line.UomName) ? "Primary" : line.UomName,
                            UomConversion.FromBase(line.QuantityReceived, legacyFactor).ToString("N4"), line.QuantityReceived.ToString("N4"),
                            "Unavailable", "Unavailable", glStatus
                        });
                        grandQtyPrimary += line.QuantityReceived;
                        hasUnresolvedValue = true;
                        continue;
                    }
                    string vendorName = vendors.GetValueOrDefault(po.VendorId, "Unknown");
                    var item = poLine != null ? items.GetValueOrDefault(poLine.ItemId) : null;
                    decimal unitCostPrimary = poLine?.UnitCost ?? 0m;
                    decimal factor = UomConversion.NormalizeFactor(line.UomConversionFactor);
                    decimal quantityInUom = UomConversion.FromBase(line.QuantityReceived, factor);
                    decimal rate = po.ExchangeRate > 0 ? po.ExchangeRate : 1m;
                    decimal unitCostInUomBase = Math.Round(UomConversion.PriceFromBase(unitCostPrimary, factor) * rate, 4);
                    decimal lineValue = Math.Round(line.QuantityReceived * unitCostPrimary * rate, 2);
                    string itemName = item?.Name ?? "Unknown";
                    string uomName = string.IsNullOrWhiteSpace(line.UomName)
                        ? item?.UoM ?? "Primary"
                        : line.UomName;

                    reportData.Rows.Add(new List<string>
                    {
                        grn.GrnNumber,
                        po.OrderNumber,
                        vendorName,
                        grn.DateReceived.ToString("MMM dd, yyyy"),
                        itemName,
                        uomName,
                        quantityInUom.ToString("N4"),
                        line.QuantityReceived.ToString("N4"),
                        unitCostInUomBase.ToString("N4"),
                        lineValue.ToString("N2"),
                        glStatus
                    });

                    grandValue += lineValue;
                    grandQtyPrimary += line.QuantityReceived;
                }
            }

            reportData.Rows.Add(new List<string>
                {
                    "GRAND TOTAL", "", "", "", "", "", "",
                    grandQtyPrimary.ToString("N4"), "", hasUnresolvedValue ? grandValue.ToString("N2") + " (known lines only)" : grandValue.ToString("N2"), ""
                });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. OVER / UNDER RECEIVING REPORT (INVOICED ONLY)
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GenerateOverUnderReceivingReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            var pos = await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId
                         && p.HasReceipt == true
                         && p.OrderDate >= startDt && p.OrderDate <= endDt)
                .ToListAsync();

            var poNumbers = pos.Select(p => p.OrderNumber).ToList();

            var invoices = await ctx.PurchaseOrders
                .AsNoTracking()
                .Where(i => i.CompanyId == companyId
                         && !string.IsNullOrEmpty(i.ConvertedFromPONumber)
                         && poNumbers.Contains(i.ConvertedFromPONumber))
                .ToDictionaryAsync(i => i.ConvertedFromPONumber!, i => i.OrderNumber);

            // FILTER: Restrict collection to invoiced entities only
            var invoicedPOs = pos.Where(p => invoices.ContainsKey(p.OrderNumber)).ToList();
            var poLineIds = invoicedPOs.SelectMany(p => p.Lines).Select(l => l.Id).ToList();

            var receivedLines = await ctx.GoodsReceiptLines
                .AsNoTracking()
                .Where(l => l.PurchaseOrderLineId.HasValue && poLineIds.Contains(l.PurchaseOrderLineId.Value))
                .ToListAsync();

            var itemIds = invoicedPOs.SelectMany(p => p.Lines).Select(l => l.ItemId).Distinct().ToList();
            var items = await ctx.Items
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name);

            var vendorIds = invoicedPOs.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Over / Under Receiving Report (Invoiced Orders)",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Invoice Order Number", "Vendor", "Item",
                    "Qty Ordered", "Qty Received", "Variance", "Status"
                },
                Rows = new List<List<string>>()
            };

            decimal totalOrdered = 0;
            decimal totalReceived = 0;

            foreach (var po in invoicedPOs.OrderByDescending(p => p.OrderDate))
            {
                string invoiceNumber = invoices[po.OrderNumber];
                string vendorName = vendors.GetValueOrDefault(po.VendorId, "Unknown");

                foreach (var poLine in po.Lines)
                {
                    string itemName = items.GetValueOrDefault(poLine.ItemId, "Unknown");
                    decimal qtyReceived = receivedLines
                        .Where(r => r.PurchaseOrderLineId == poLine.Id)
                        .Sum(r => r.QuantityReceived);
                    decimal variance = qtyReceived - poLine.QuantityOrdered;

                    string status = variance == 0 ? "Exact Match"
                                  : variance > 0 ? "Over-Received"
                                  : "Under-Received";

                    reportData.Rows.Add(new List<string>
                    {
                        invoiceNumber,
                        vendorName,
                        itemName,
                        poLine.QuantityOrdered.ToString("N2"),
                        qtyReceived.ToString("N2"),
                        variance.ToString("N2"),
                        status
                    });

                    totalOrdered += poLine.QuantityOrdered;
                    totalReceived += qtyReceived;
                }
            }

            decimal totalVariance = totalReceived - totalOrdered;
            reportData.Rows.Add(new List<string>
            {
                "", "GRAND TOTAL", "",
                totalOrdered.ToString("N2"),
                totalReceived.ToString("N2"),
                totalVariance.ToString("N2"),
                ""
            });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 4. PARTIALLY RECEIVED POs (INVOICED ONLY)
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GeneratePartiallyReceivedPOsReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            var pos = await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId
                         && p.HasReceipt == true
                         && p.IsFullyReceived == false
                         && p.Status != PurchaseOrderStatus.Closed
                         && p.OrderDate >= startDt && p.OrderDate <= endDt)
                .OrderBy(p => p.OrderDate)
                .ToListAsync();

            var poNumbers = pos.Select(p => p.OrderNumber).ToList();

            var invoices = await ctx.PurchaseOrders
                .AsNoTracking()
                .Where(i => i.CompanyId == companyId
                         && !string.IsNullOrEmpty(i.ConvertedFromPONumber)
                         && poNumbers.Contains(i.ConvertedFromPONumber))
                .ToDictionaryAsync(i => i.ConvertedFromPONumber!, i => i.OrderNumber);

            // FILTER: Keep only partially received POs that have been invoiced
            var invoicedPOs = pos.Where(p => invoices.ContainsKey(p.OrderNumber)).ToList();
            var poLineIds = invoicedPOs.SelectMany(p => p.Lines).Select(l => l.Id).ToList();

            var receivedLines = await ctx.GoodsReceiptLines
                .AsNoTracking()
                .Where(l => l.PurchaseOrderLineId.HasValue && poLineIds.Contains(l.PurchaseOrderLineId.Value))
                .ToListAsync();

            var vendorIds = invoicedPOs.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Partially Received Purchase Orders (Invoiced)",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Invoice Order Number", "Vendor", "Order Date",
                    "Total Ordered", "Total Received", "Remaining", "% Received", "Order Value"
                },
                Rows = new List<List<string>>()
            };

            decimal grandOrderValue = 0;

            foreach (var po in invoicedPOs)
            {
                string invoiceNumber = invoices[po.OrderNumber];
                var lineIds = po.Lines.Select(l => l.Id).ToHashSet();

                decimal qtyOrdered = po.Lines.Sum(l => l.QuantityOrdered);
                decimal qtyReceived = receivedLines
                    .Where(r => r.PurchaseOrderLineId.HasValue && lineIds.Contains(r.PurchaseOrderLineId.Value))
                    .Sum(r => r.QuantityReceived);

                decimal remaining = qtyOrdered - qtyReceived;
                decimal pct = qtyOrdered > 0 ? (qtyReceived / qtyOrdered) * 100 : 0;
                decimal orderValue = po.Lines.Sum(l => l.LineTotal) * po.ExchangeRate;

                reportData.Rows.Add(new List<string>
                {
                    invoiceNumber,
                    vendors.GetValueOrDefault(po.VendorId, "Unknown"),
                    po.OrderDate.ToString("MMM dd, yyyy"),
                    qtyOrdered.ToString("N2"),
                    qtyReceived.ToString("N2"),
                    remaining.ToString("N2"),
                    $"{pct:N1}%",
                    orderValue.ToString("N2")
                });

                grandOrderValue += orderValue;
            }

            reportData.Rows.Add(new List<string>
                { "", "GRAND TOTAL", "", "", "", "", "", grandOrderValue.ToString("N2") });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 5. VENDOR PAYMENT HISTORY
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GenerateVendorPaymentHistoryReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            // 1. Load historical payment segments within the date window, stripping out UI Workspace drafts
            var allPayments = await ctx.Set<VendorPayment>()
                .AsNoTracking()
                .Where(p => p.Date >= startDt && p.Date <= endDt
                         && (p.Reference == null || !p.Reference.ToLower().StartsWith("draft-"))) // <-- FIXED TRANSLATION HERE
                .OrderByDescending(p => p.Date)
                .ToListAsync();

            var billIds = allPayments.Select(p => p.PurchaseInvoiceId).Distinct().ToList();

            // 2. Cross-reference against Posted Vendor Bills ONLY (drops all unposted bill drafts)
            var bills = await ctx.PurchaseInvoices
                .AsNoTracking()
                .Where(b => billIds.Contains(b.Id) && b.CompanyId == companyId && b.IsPosted == true)
                .ToDictionaryAsync(b => b.Id);

            var companyBillIds = bills.Keys.ToHashSet();

            // 3. FILTER: Keep payments only if they belong to a verified posted bill partition
            var postedPayments = allPayments
                .Where(p => companyBillIds.Contains(p.PurchaseInvoiceId))
                .ToList();

            var vendorIds = bills.Values.Select(b => b.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var glIds = postedPayments.Select(p => p.BankGlAccountId).Distinct().ToList();
            var glAccounts = await ctx.SegChartOfAccounts
                .Where(a => glIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Description);

            var reportData = new StandardReportData
            {
                ReportName = "Vendor Payment History",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Payment Date", "Vendor", "Invoice Order Number",
                    "Payment Reference", "Bank Account", "Amount Paid"
                },
                Rows = new List<List<string>>()
            };

            decimal grandTotal = 0;

            foreach (var payment in postedPayments)
            {
                var bill = bills.GetValueOrDefault(payment.PurchaseInvoiceId);
                string vendorName = bill != null ? vendors.GetValueOrDefault(bill.VendorId, "Unknown") : "Unknown";
                string bankAcct = glAccounts.GetValueOrDefault(payment.BankGlAccountId, "Unknown Account");

                // Format negative allocations (debit note reversals) into (XXX.XX) bracket notations
                string formattedAmount = payment.Amount < 0
                    ? $"({Math.Abs(payment.Amount):N2})"
                    : payment.Amount.ToString("N2");

                reportData.Rows.Add(new List<string>
                {
                    payment.Date.ToString("MMM dd, yyyy"),
                    vendorName,
                    bill?.ExternalInvoiceNumber ?? "N/A",
                    payment.Reference ?? "",
                    bankAcct,
                    formattedAmount
                });

                grandTotal += payment.Amount;
            }

            // Apply bracket format to grand total if cumulative operations trend negative
            string formattedGrandTotal = grandTotal < 0
                ? $"({Math.Abs(grandTotal):N2})"
                : grandTotal.ToString("N2");

            reportData.Rows.Add(new List<string>
                { "", "GRAND TOTAL", "", "", "", formattedGrandTotal });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 6. PURCHASE SPEND BY VENDOR (INVOICED POs ONLY)
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GeneratePurchaseSpendByVendorReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            // 1. Fetch posted vendor bills within the range
            var bills = await ctx.PurchaseInvoices
                .AsNoTracking()
                .Include(b => b.Payments)
                .Where(b => b.CompanyId == companyId
                         && b.IsPosted == true
                         && b.BillDate >= startDt && b.BillDate <= endDt)
                .ToListAsync();

            // 2. Identify which of these bills are linked to a PO pipeline via ConvertedFromPONumber
            var invoiceNumbers = bills.Select(b => b.ExternalInvoiceNumber).Distinct().ToList();

            var invoicedPOsDict = await ctx.PurchaseOrders
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId
                         && !string.IsNullOrEmpty(p.ConvertedFromPONumber)
                         && invoiceNumbers.Contains(p.OrderNumber))
                .ToDictionaryAsync(p => p.OrderNumber); // Keyed by the Invoice Order Number

            // FILTER: Keep only the bills that exist as a valid Invoice Order in the PO pipeline
            var filteredBills = bills
                .Where(b => !string.IsNullOrEmpty(b.ExternalInvoiceNumber) && invoicedPOsDict.ContainsKey(b.ExternalInvoiceNumber))
                .ToList();

            var vendorIds = filteredBills.Select(b => b.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Purchase Spend by Vendor (Invoiced Orders Only)",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Vendor", "No. of Bills",
                    "Total Billed", "Total Paid", "Outstanding Balance"
                },
                Rows = new List<List<string>>()
            };

            // Group the filtered invoiced bills by Vendor
            var grouped = filteredBills
                .GroupBy(b => b.VendorId)
                .Select(g => new
                {
                    VendorId = g.Key,
                    BillCount = g.Count(),
                    TotalBilled = g.Sum(b => b.TotalAmount),
                    TotalPaid = g.Sum(b => b.Payments.Sum(p => p.Amount))
                })
                .OrderByDescending(g => g.TotalBilled)
                .ToList();

            decimal grandBilled = 0, grandPaid = 0;

            foreach (var row in grouped)
            {
                decimal outstanding = row.TotalBilled - row.TotalPaid;

                // Handle standard bracket formatting for any outstanding negative balances
                string formattedBilled = row.TotalBilled.ToString("N2");
                string formattedPaid = row.TotalPaid.ToString("N2");
                string formattedOutstanding = outstanding < 0
                    ? $"({Math.Abs(outstanding):N2})"
                    : outstanding.ToString("N2");

                reportData.Rows.Add(new List<string>
                {
                    vendors.GetValueOrDefault(row.VendorId, "Unknown"),
                    row.BillCount.ToString("N0"),
                    formattedBilled,
                    formattedPaid,
                    formattedOutstanding
                });

                grandBilled += row.TotalBilled;
                grandPaid += row.TotalPaid;
            }

            decimal grandOutstanding = grandBilled - grandPaid;
            string formattedGrandOutstanding = grandOutstanding < 0
                ? $"({Math.Abs(grandOutstanding):N2})"
                : grandOutstanding.ToString("N2");

            reportData.Rows.Add(new List<string>
            {
                "GRAND TOTAL", "",
                grandBilled.ToString("N2"),
                grandPaid.ToString("N2"),
                formattedGrandOutstanding
            });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 7. DISCOUNT RECEIVED SUMMARY (INVOICED ONLY)
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GenerateDiscountReceivedSummaryReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            var pos = await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId
                         && (p.DiscountAmount > 0 || p.DiscountPercentage > 0)
                         && p.OrderDate >= startDt && p.OrderDate <= endDt)
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();

            var poNumbers = pos.Select(p => p.OrderNumber).ToList();

            var invoices = await ctx.PurchaseOrders
                .AsNoTracking()
                .Where(i => i.CompanyId == companyId
                         && !string.IsNullOrEmpty(i.ConvertedFromPONumber)
                         && poNumbers.Contains(i.ConvertedFromPONumber))
                .ToDictionaryAsync(i => i.ConvertedFromPONumber!, i => i.OrderNumber);

            // FILTER: Select only invoiced POs with promotional structures applied
            var invoicedPOs = pos.Where(p => invoices.ContainsKey(p.OrderNumber)).ToList();

            var vendorIds = invoicedPOs.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Discount Received Summary (Invoiced Orders)",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Invoice Order Number", "Vendor", "Order Date",
                    "Gross Value", "Discount Type", "Discount Amount", "Net Value"
                },
                Rows = new List<List<string>>()
            };

            decimal grandGross = 0;
            decimal grandDiscount = 0;

            foreach (var po in invoicedPOs)
            {
                string invoiceNumber = invoices[po.OrderNumber];
                decimal grossForeign = po.Lines.Sum(l => l.LineTotal);
                decimal grossBase = Math.Round(grossForeign * po.ExchangeRate, 2);

                decimal discountForeign = po.DiscountAmount;
                if (po.DiscountPercentage > 0)
                    discountForeign = grossForeign * (po.DiscountPercentage / 100);

                decimal discountBase = Math.Round(discountForeign * po.ExchangeRate, 2);
                decimal netBase = grossBase - discountBase;
                string discType = po.DiscountPercentage > 0 ? $"{po.DiscountPercentage:N2}%" : "Fixed Amount";

                reportData.Rows.Add(new List<string>
                {
                    invoiceNumber,
                    vendors.GetValueOrDefault(po.VendorId, "Unknown"),
                    po.OrderDate.ToString("MMM dd, yyyy"),
                    grossBase.ToString("N2"),
                    discType,
                    discountBase.ToString("N2"),
                    netBase.ToString("N2")
                });

                grandGross += grossBase;
                grandDiscount += discountBase;
            }

            reportData.Rows.Add(new List<string>
            {
                "", "GRAND TOTAL", "",
                grandGross.ToString("N2"), "",
                grandDiscount.ToString("N2"),
                (grandGross - grandDiscount).ToString("N2")
            });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 8. THREE-WAY MATCH EXCEPTION REPORT
        //    Lists every vendor bill flagged with a MatchStatus of Variance,
        //    along with the reason, for AP approval/investigation workflows.
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GenerateThreeWayMatchExceptionReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            var bills = await ctx.PurchaseInvoices
                .AsNoTracking()
                .Where(b => b.CompanyId == companyId
                         && b.MatchStatus == BillMatchStatus.Variance
                         && b.BillDate >= startDt && b.BillDate <= endDt)
                .OrderByDescending(b => b.BillDate)
                .ToListAsync();

            var vendorIds = bills.Select(b => b.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var poIds = bills
                .Where(b => b.PurchaseOrderId.HasValue)
                .Select(b => b.PurchaseOrderId!.Value)
                .Distinct()
                .ToList();

            var pos = await ctx.PurchaseOrders
                .Where(p => poIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.OrderNumber);

            var reportData = new StandardReportData
            {
                ReportName = "Three-Way Match Exception Report",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Bill Date", "Bill Reference", "Vendor",
                    "Linked PO", "Bill Amount", "Variance Reason"
                },
                Rows = new List<List<string>>()
            };

            decimal grandTotal = 0;

            foreach (var bill in bills)
            {
                string poNumber = bill.PurchaseOrderId.HasValue
                    ? pos.GetValueOrDefault(bill.PurchaseOrderId.Value, "N/A")
                    : "No PO Linked";

                reportData.Rows.Add(new List<string>
                {
                    bill.BillDate.ToString("MMM dd, yyyy"),
                    bill.ExternalInvoiceNumber ?? "N/A",
                    vendors.GetValueOrDefault(bill.VendorId, "Unknown"),
                    poNumber,
                    bill.TotalAmount.ToString("N2"),
                    bill.MatchVarianceReason ?? ""
                });

                grandTotal += bill.TotalAmount;
            }

            reportData.Rows.Add(new List<string>
                { "", "GRAND TOTAL", "", "", grandTotal.ToString("N2"), "" });

            return reportData;
        }
        // =================================================================
        // ACCOUNTS PAYABLE DEBIT NOTE (PAYMENT REVERSAL ENGINE - NO STOCK)
        // =================================================================
        public async Task<string> PostVendorPaymentReversalAsync(VendorPayment reversalPayment, Guid companyId, string userId)
        {
            if (reversalPayment.Amount <= 0) return "STOP: Reversal allocation amount must be greater than zero.";
            if (reversalPayment.BankGlAccountId == Guid.Empty) return "STOP: Valid target Refund Bank/Asset GL account mapping is required.";

            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                // Load the target vendor bill complete with its historical payments loop trace
                var bill = await ctx.PurchaseInvoices
                    .Include(b => b.Payments)
                    .FirstOrDefaultAsync(b => b.Id == reversalPayment.PurchaseInvoiceId && b.CompanyId == companyId);

                if (bill == null) return "STOP: Target Vendor Bill not found or access boundary denied.";
                if (!bill.IsPosted) return "STOP: Cannot reverse payments against an unposted draft ledger bill.";

                decimal rate = bill.ExchangeRate > 0 ? bill.ExchangeRate : 1.0m;

    // Calculate the real ceiling limit for this reversal based on Foreign currency inputs
    decimal totalHistoricallyPaidBase = bill.Payments.Sum(p => p.Amount);
    decimal totalHistoricallyPaidForeign = Math.Round(totalHistoricallyPaidBase / rate, 2);

                if (reversalPayment.Amount > totalHistoricallyPaidForeign)
                {
                    return $"3-Way Match Exception: Reversal allocation ({reversalPayment.Amount:N2}) exceeds the total historical cash accumulated on this bill ({totalHistoricallyPaidForeign:N2} {bill.ExternalInvoiceNumber}).";
                }

// --- CRITICAL MULTI-CURRENCY FIX ---
// Calculate the true base value hitting your General Ledger and Vendor Master ledgers
decimal finalAmountBase = Math.Round(reversalPayment.Amount * rate, 2);

// Enforce proper accounting polarity by transforming the numeric inputs to negative markers
var finalizedReversalLine = new VendorPayment
{
    Id = Guid.NewGuid(),
    PurchaseInvoiceId = bill.Id,
    ProjectId = bill.ProjectId,
    Date = reversalPayment.Date,
    Amount = -finalAmountBase, // STORE TRUE BASE CURRENCY WITH NEGATIVE POLARITY
    BankGlAccountId = reversalPayment.BankGlAccountId,
    Reference = string.IsNullOrWhiteSpace(reversalPayment.Reference)
        ? $"REV-{bill.ExternalInvoiceNumber}"
        : reversalPayment.Reference
};

ctx.Set<VendorPayment>().Add(finalizedReversalLine);

// --- DOUBLE-ENTRY JOURNAL DISTRIBUTION ---
var glLines = new List<GLJournalLine>
                {
                    // 1. DEBIT: Bank/Cash Equivalents account (Refunding your base functional assets)
                    new GLJournalLine
                    {
                        SegCoaId = finalizedReversalLine.BankGlAccountId,
                        Debit = finalAmountBase, // Correctly applies multiplied baseline values
                        Credit = 0,
                        Reference = $"Refund Inflow: {finalizedReversalLine.Reference}"
                    },

                    // 2. CREDIT: Accounts Payable Trade Liability (Restoring your outstanding debt to supplier)
                    new GLJournalLine
                    {
                        SegCoaId = bill.AccountsPayableGlId,
                        Debit = 0,
                        Credit = finalAmountBase, // Balanced base value entry
                        Reference = $"AP Debt Restored: {finalizedReversalLine.Reference}"
                    }
                };

// Stream the array directly to your transactional GL Posting Core
var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(
    companyId,
    DateOnly.FromDateTime(finalizedReversalLine.Date),
    "AP Debit Note Reversal",
    finalizedReversalLine.Reference,
    glLines,
    userId,
    inheritedProjectId: bill.ProjectId
);

if (!string.IsNullOrEmpty(glErr)) throw new Exception($"General Ledger Engine Rejection: {glErr}");
if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value, userId);

// --- DOWNSTREAM PROCUREMENT LINE STATUS ROLLBACKS ---
if (bill.PurchaseOrderId.HasValue)
{
    var po = await ctx.PurchaseOrders.FindAsync(bill.PurchaseOrderId.Value);
    if (po != null)
    {
        po.IsFullyPaid = false;

        decimal prospectiveNewPaidTotalBase = totalHistoricallyPaidBase - finalAmountBase;

        po.Status = prospectiveNewPaidTotalBase <= 0
            ? PurchaseOrderStatus.Open
            : PurchaseOrderStatus.PartiallyReceived;
    }
}

await ctx.SaveChangesAsync();
await tx.CommitAsync();
return string.Empty;
            }
            catch (Exception ex)
            {
    await tx.RollbackAsync();
    return $"Debit Note Reversal Crash: {ex.Message}";
}
        }


    }
}
