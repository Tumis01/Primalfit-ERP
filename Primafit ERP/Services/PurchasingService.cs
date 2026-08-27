using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PurchasingService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly InventoryService _inventoryService;
        private readonly GLOperationsService _glOps;
        private readonly BudgetService _budgetService;
        private readonly InventoryValuationService _valuationService;
        private readonly TransactionMappingService _mappingService; // <-- NEW: Injected Mapping Service

        public PurchasingService(
            IDbContextFactory<AppDbContext> dbFactory,
        InventoryService inventoryService,
        GLOperationsService glOps,
        BudgetService budgetService,
        InventoryValuationService valuationService,
        TransactionMappingService mappingService) // <-- NEW
        {
            _dbFactory = dbFactory;
            _inventoryService = inventoryService;
            _glOps = glOps;
            _budgetService = budgetService;
            _valuationService = valuationService;
            _mappingService = mappingService; // <-- NEW
        }

        public async Task<string> SaveVendorBillAsync(VendorBill bill)
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
                bool invoiceExists = await ctx.VendorBills
                    .AnyAsync(b => b.CompanyId == bill.CompanyId
                                && b.VendorId == bill.VendorId
                                && b.ExternalInvoiceNumber.ToLower() == bill.ExternalInvoiceNumber.ToLower()
                                && b.Id != bill.Id); // Exclude self if updating an existing draft

                if (invoiceExists)
                    return $"STOP: Invoice number '{bill.ExternalInvoiceNumber}' has already been recorded for this vendor.";
            }

            var existing = await ctx.VendorBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == bill.Id);

            try
            {
                if (existing == null)
                {
                    if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();

                    foreach (var line in bill.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.VendorBillId = bill.Id;
                    }

                    ctx.VendorBills.Add(bill);
                    ctx.VendorBillLines.AddRange(bill.Lines);
                }
                else
                {
                    if (existing.IsPosted) return "STOP: Cannot edit a bill that has already been posted.";

                    bill.CompanyId = existing.CompanyId;
                    bill.IsPosted = existing.IsPosted;
                    bill.PostedDate = existing.PostedDate;

                    ctx.Entry(existing).CurrentValues.SetValues(bill);
                    ctx.VendorBillLines.RemoveRange(existing.Lines);

                    foreach (var line in bill.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.VendorBillId = bill.Id;
                    }
                    ctx.VendorBillLines.AddRange(bill.Lines);
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

            var bill = await ctx.VendorBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null) return "Bill not found.";

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

            if (po.VendorId == Guid.Empty) return "Vendor is required.";
            if (po.Lines.Count == 0) return "Order must have at least one line.";

            var existing = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == po.Id);

            try
            {
                if (existing == null)
                {
                    if (po.Id == Guid.Empty) po.Id = Guid.NewGuid();

                    // --- GENERATE NUMBER BASED ON STATUS WITH COLLISION LOOP ---
                    if (string.IsNullOrWhiteSpace(po.OrderNumber))
                    {
                        string prefix = po.Status == PurchaseOrderStatus.Request ? "REQ" : "PO";
                        bool isDuplicate = true;
                        string generatedNumber = string.Empty;

                        // Keep regenerating if a random number collides inside this month
                        while (isDuplicate)
                        {
                            generatedNumber = $"{prefix}-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                            isDuplicate = await ctx.PurchaseOrders.AnyAsync(p => p.CompanyId == po.CompanyId && p.OrderNumber == generatedNumber);
                        }
                        po.OrderNumber = generatedNumber;
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
                    if (existing.IsInvoicePosted || existing.HasReceipt)
                        return "Cannot edit an order that has already been received or invoiced.";

                    if (existing.Status == PurchaseOrderStatus.Request)
                    {
                        bool isConverted = await ctx.PurchaseOrders.AnyAsync(p => p.ConvertedFromRequestNumber == existing.OrderNumber);
                        if (isConverted) return "Cannot edit a Request that has already been converted to an Order.";
                    }

                    po.CompanyId = existing.CompanyId;
                    po.OrderNumber = existing.OrderNumber;
                    po.ConvertedFromRequestNumber = existing.ConvertedFromRequestNumber;

                    ctx.Entry(existing).CurrentValues.SetValues(po);
                    ctx.PurchaseOrderLines.RemoveRange(existing.Lines);

                    foreach (var line in po.Lines)
                    {
                        line.PurchaseOrderId = po.Id;
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        ctx.PurchaseOrderLines.Add(line);
                    }
                }

                await ctx.SaveChangesAsync();
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

            // --- FIXED: COLLISION CONTROL LOOP FOR AUTOGENERATED NUMBER ---
            bool isDuplicate = true;
            string generatedPoNumber = string.Empty;

            while (isDuplicate)
            {
                generatedPoNumber = $"PO-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                // Enforce uniqueness within the target company boundary
                isDuplicate = await ctx.PurchaseOrders.AnyAsync(o => o.CompanyId == req.CompanyId && o.OrderNumber == generatedPoNumber);
            }

            var order = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = req.CompanyId,
                OrderNumber = generatedPoNumber, // Assigned safely via the collision check
                ConvertedFromRequestNumber = req.OrderNumber,
                TaxId = req.TaxId,
                TaxGLAccountId = req.TaxGLAccountId,
                VendorId = req.VendorId,
                OrderDate = DateTime.Today,
                Status = PurchaseOrderStatus.Open,
                CurrencyId = req.CurrencyId,
                ExchangeRate = req.ExchangeRate,
                // Inherit Discounts
                DiscountPercentage = req.DiscountPercentage,
                DiscountAmount = req.DiscountAmount,
                DiscountGlAccountId = req.DiscountGlAccountId
            };

            foreach (var line in req.Lines)
            {
                order.Lines.Add(new PurchaseOrderLine
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = order.Id,
                    ItemId = line.ItemId,
                    QuantityOrdered = line.QuantityOrdered,
                    UnitCost = line.UnitCost
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

            // --- ENFORCE UNIQUE NUMBER GENERATION LOOP ---
            bool isDuplicate = true;
            string generatedInvoiceNumber = string.Empty;

            while (isDuplicate)
            {
                generatedInvoiceNumber = $"INV-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}";
                isDuplicate = await ctx.PurchaseOrders.AnyAsync(o => o.CompanyId == po.CompanyId && o.OrderNumber == generatedInvoiceNumber);
            }

            var invoice = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = po.CompanyId,
                OrderNumber = generatedInvoiceNumber, // Assigned safely via the collision check
                ConvertedFromPONumber = po.OrderNumber,
                TaxId = po.TaxId,
                TaxGLAccountId = po.TaxGLAccountId,
                VendorId = po.VendorId,
                OrderDate = DateTime.Today,
                Status = PurchaseOrderStatus.DraftInvoice, // Initial state inside the Invoice workspace
                CurrencyId = po.CurrencyId,
                ExchangeRate = po.ExchangeRate,
                DiscountPercentage = po.DiscountPercentage,
                DiscountAmount = po.DiscountAmount,
                DiscountGlAccountId = po.DiscountGlAccountId
            };

            foreach (var line in po.Lines)
            {
                invoice.Lines.Add(new PurchaseOrderLine
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = invoice.Id,
                    ItemId = line.ItemId,
                    QuantityOrdered = line.QuantityOrdered,
                    UnitCost = line.UnitCost
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
                if (invoiceOrder.Status == PurchaseOrderStatus.Invoiced) return "This invoice has already been posted to the ledger.";

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

                var bill = new VendorBill
                {
                    Id = Guid.NewGuid(),
                    CompanyId = invoiceOrder.CompanyId,
                    VendorId = invoiceOrder.VendorId,
                    PurchaseOrderId = invoiceOrder.Id,
                    AccountsPayableGlId = resolvedApAccount,
                    ExternalInvoiceNumber = invoiceOrder.OrderNumber,
                    BillDate = DateTime.Today,
                    CurrencyId = invoiceOrder.CurrencyId,
                    ExchangeRate = invoiceOrder.ExchangeRate,
                    IsPosted = true,
                    PostedDate = DateTime.Now,
                    MatchStatus = BillMatchStatus.Matched
                };

                decimal totalGrossForeign = 0;

                foreach (var line in invoiceOrder.Lines)
                {
                    if (line.QuantityOrdered > 0)
                    {
                        bill.Lines.Add(new VendorBillLine
                        {
                            Id = Guid.NewGuid(),
                            VendorBillId = bill.Id,
                            ItemId = line.ItemId,
                            QuantityBilled = line.QuantityOrdered,
                            UnitCostBilled = line.UnitCost,
                            ExpenseGlAccountId = resolvedClearingAccount
                        });
                        totalGrossForeign += (line.QuantityOrdered * line.UnitCost);
                        line.QuantityBilled = line.QuantityOrdered;
                    }
                }

                decimal discountForeign = invoiceOrder.DiscountAmount;
                if (invoiceOrder.DiscountPercentage > 0)
                {
                    discountForeign = totalGrossForeign * (invoiceOrder.DiscountPercentage / 100);
                }
                decimal netForeign = totalGrossForeign - discountForeign;

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

                ctx.VendorBills.Add(bill);
                ctx.VendorBillLines.AddRange(bill.Lines);

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

                var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(invoiceOrder.CompanyId, DateOnly.FromDateTime(bill.BillDate), "Vendor Bill Post", $"Inv {bill.ExternalInvoiceNumber}", glLines, userId);
                if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                if (batchId.HasValue) await _glOps.PostBatchAsync(invoiceOrder.CompanyId, batchId.Value, userId);

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

        public async Task<string> SaveGoodsReceiptAsync(GoodsReceipt grn, Guid warehouseId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();

            try
            {
                if (grn.CompanyId == Guid.Empty) return "STOP: No CompanyId.";
                if (grn.PurchaseOrderId == Guid.Empty) return "STOP: No Purchase Order selected.";
                if (warehouseId == Guid.Empty) return "STOP: No Warehouse selected.";

                var po = await ctx.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId);
                if (po == null) return "STOP: PO not found.";

                // Validate Quantities to prevent over-receiving
                var poLineIds = po.Lines.Select(l => l.Id).ToList();
                var pastReceipts = await ctx.GoodsReceiptLines.Where(l => poLineIds.Contains(l.PurchaseOrderLineId)).ToListAsync();

                foreach (var line in grn.Lines)
                {
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                    if (poLine == null) continue;

                    decimal pastQty = pastReceipts.Where(p => p.PurchaseOrderLineId == line.PurchaseOrderLineId).Sum(p => p.QuantityReceived);
                    decimal maxAllowed = poLine.QuantityOrdered - pastQty;

                    if (line.QuantityReceived > maxAllowed) return $"STOP: Cannot receive more than ordered. Max allowed is {maxAllowed}.";
                }

                if (grn.Id == Guid.Empty) grn.Id = Guid.NewGuid();
                if (string.IsNullOrWhiteSpace(grn.GrnNumber)) grn.GrnNumber = $"GRN-{DateTime.Now:yyMM}-{Random.Shared.Next(100, 999)}";

                foreach (var line in grn.Lines) line.Id = Guid.NewGuid();

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

                ctx.GoodsReceipts.Add(grn);

                var glLines = new List<GLJournalLine>();
                decimal totalReceivedValueBase = 0;

                // 3. Post Inventory & Financials
                foreach (var grnLine in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == grnLine.PurchaseOrderLineId);
                    if (poLine == null) continue;

                    var item = await ctx.Items.FindAsync(poLine.ItemId);
                    if (item == null) continue;

                    decimal lineValueForeign = grnLine.QuantityReceived * poLine.UnitCost;
                    decimal lineValueBase = Math.Round(lineValueForeign * po.ExchangeRate, 2);
                    totalReceivedValueBase += lineValueBase;

                    if (!item.IsService)
                    {
                        // A. Physical Stock Increase
                        ctx.StockLedgers.Add(new StockLedger
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = grn.CompanyId,
                            WarehouseId = warehouseId,
                            ItemId = poLine.ItemId,
                            Date = grn.DateReceived,
                            Reference = grn.GrnNumber,
                            Type = StockMovementType.Purchase,
                            QuantityChanged = grnLine.QuantityReceived,
                            CostAtTime = poLine.UnitCost
                        });

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
                        {
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
                        {
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

                    var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(
                        grn.CompanyId,
                        DateOnly.FromDateTime(grn.DateReceived),
                        "Goods Receipt",
                        $"GRN {grn.GrnNumber}",
                        glLines,
                        userId);

                    if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                    if (batchId.HasValue) await _glOps.PostBatchAsync(grn.CompanyId, batchId.Value, userId);
                }

                // Update PO Status Tracking
                po.HasReceipt = true;
                decimal totalOrdered = po.Lines.Sum(l => l.QuantityOrdered);
                decimal totalCurrentlyReceiving = grn.Lines.Sum(l => l.QuantityReceived);
                decimal totalPastReceived = pastReceipts.Sum(l => l.QuantityReceived);

                if ((totalPastReceived + totalCurrentlyReceiving) >= totalOrdered) po.IsFullyReceived = true;

                if (po.Status != PurchaseOrderStatus.DraftInvoice && po.Status != PurchaseOrderStatus.Invoiced)
                {
                    po.Status = PurchaseOrderStatus.PartiallyReceived;
                }

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

        public async Task<List<PurchaseOrder>> GetOpenPOsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId && p.Status != PurchaseOrderStatus.Closed)
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();
        }
        public async Task<List<PurchaseOrder>> GetPOsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId)
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();
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
            return await ctx.GoodsReceipts.AnyAsync(g => g.PurchaseOrderId == poId);
        }
        public async Task<string> PostVendorBillAsync(Guid billId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var bill = await ctx.VendorBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null) return "Bill not found.";
            if (bill.IsPosted) return "STOP: This bill has already been posted.";

            // 1. Resolve Accounts Payable Liability via Mapping Router (Credit leg)
            Guid apAccount = await _mappingService.GetMappedAccountAsync(
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
                Guid expenseAccount = await _mappingService.GetMappedAccountAsync(
                    bill.CompanyId,
                    SystemTransactionType.PurchaseInvoice,
                    isDebit: true,
                    defaultAccountId: line.ExpenseGlAccountId);

                if (expenseAccount == Guid.Empty) return "STOP: Line missing Expense Account.";

                decimal lineTotalBase = Math.Round((line.QuantityBilled * line.UnitCostBilled) * bill.ExchangeRate, 2);
                totalDebitsBase += lineTotalBase;

                string glRef = !string.IsNullOrWhiteSpace(line.Description) ? line.Description : $"Bill: {bill.ExternalInvoiceNumber}";

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = expenseAccount,
                    Debit = lineTotalBase,
                    Credit = 0,
                    Reference = glRef
                });
            }

            // 3. Tax Debits
            if (bill.TaxId.HasValue && bill.TaxGLAccountId.HasValue)
            {
                var tax = await ctx.Taxes.FindAsync(bill.TaxId);
                if (tax != null)
                {
                    decimal subTotalForeign = bill.Lines.Sum(l => l.QuantityBilled * l.UnitCostBilled);
                    decimal taxForeign = subTotalForeign * (tax.Per / 100);
                    decimal taxBase = Math.Round(taxForeign * bill.ExchangeRate, 2);

                    totalDebitsBase += taxBase;

                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = bill.TaxGLAccountId.Value,
                        Debit = taxBase,
                        Credit = 0,
                        Reference = $"Input Tax: {bill.ExternalInvoiceNumber}"
                    });
                }
            }

            // 4. Credit AP Liability
            bill.TotalAmount = totalDebitsBase;
            bill.AccountsPayableGlId = apAccount; // Record the resolved account on the bill

            glLines.Add(new GLJournalLine
            {
                SegCoaId = apAccount,
                Debit = 0,
                Credit = totalDebitsBase,
                Reference = $"Inv #{bill.ExternalInvoiceNumber} - {vendorName}"
            });

            // 5. Create and Post Journal Batch
            var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                bill.CompanyId, postDate, "Vendor Bill",
                $"Inv #{bill.ExternalInvoiceNumber ?? "REF"}", glLines, userId
            );

            if (!string.IsNullOrEmpty(err)) return $"GL ERROR: {err}";

            if (batchId.HasValue)
            {
                var postErr = await _glOps.PostBatchAsync(bill.CompanyId, batchId.Value, userId);
                if (!string.IsNullOrEmpty(postErr)) return $"GL Engine Rejected Posting: {postErr}";
            }

            bill.IsPosted = true;
            bill.PostedDate = DateTime.Now;

            if (bill.PurchaseOrderId.HasValue)
            {
                var po = await ctx.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value);
                if (po != null) po.IsInvoicePosted = true;
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> PostVendorPaymentAsync(VendorPayment payment, Guid companyId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var bill = await ctx.VendorBills
                    .Include(b => b.Payments)
                    .FirstOrDefaultAsync(b => b.Id == payment.VendorBillId && b.CompanyId == companyId);

                if (bill == null) return "Vendor Bill not found.";
                if (!bill.IsPosted) return "Cannot pay an unposted draft bill.";
                if (payment.Amount <= 0) return "Payment amount must be greater than zero.";

                decimal currentPaid = bill.Payments.Sum(p => p.Amount);
                if (currentPaid + payment.Amount > bill.TotalAmount + 0.01m)
                    return $"Payment of {payment.Amount:N2} exceeds remaining balance of {(bill.TotalAmount - currentPaid):N2}.";

                // 1. Resolve custom mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (payment.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.CustomTransactionTypeId == payment.CustomTransactionTypeId.Value);
                }

                // 2. DEBIT LEG: Accounts Payable (Liability decreases)
                Guid debitApAccountId = payment.OverrideDebitApGlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? await _mappingService.GetMappedAccountAsync(
                        companyId,
                        SystemTransactionType.VendorPayment,
                        isDebit: true,
                        defaultAccountId: bill.AccountsPayableGlId);

                // 3. CREDIT LEG: Bank / Cash Asset (Asset decreases)
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

                if (debitApAccountId == Guid.Empty)
                    return "Configuration Error: Missing Accounts Payable Account for payment debit.";

                if (creditBankAccountId == Guid.Empty)
                    return "Configuration Error: Missing Bank / Cash GL Account for payment credit.";

                bool apExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == debitApAccountId && a.CompanyId == companyId && a.IsActive);
                if (!apExists) return "Accounts Payable Account is invalid or inactive.";

                bool bankExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == creditBankAccountId && a.CompanyId == companyId && a.IsActive);
                if (!bankExists) return "Bank Account is invalid or inactive.";

                payment.BankGlAccountId = creditBankAccountId;
                if (payment.Id == Guid.Empty) payment.Id = Guid.NewGuid();
                ctx.Set<VendorPayment>().Add(payment);

                // 4. Balanced GL Lines: DR AP | CR Bank
                var glLines = new List<GLJournalLine>
        {
            new GLJournalLine
            {
                SegCoaId = debitApAccountId,
                Debit = payment.Amount,
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

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    companyId,
                    DateOnly.FromDateTime(payment.Date),
                    "Vendor Payment",
                    string.IsNullOrWhiteSpace(payment.Reference) ? $"Pay {bill.ExternalInvoiceNumber}" : payment.Reference,
                    glLines,
                    userId);

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value, userId);

                if (bill.PurchaseOrderId.HasValue && (currentPaid + payment.Amount) >= bill.TotalAmount - 0.01m)
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

            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == poId);

            if (po == null) return "Purchase Order not found.";

            // SECURITY PRECAUTION: Prevent deleting POs with financial/inventory impact
            if (po.HasReceipt || po.IsInvoicePosted)
            {
                return "STOP: Cannot delete a Purchase Order that has already received goods or been billed. It must be kept for auditing.";
            }

            try
            {
                // Remove the child line items first, then the header
                ctx.PurchaseOrderLines.RemoveRange(po.Lines);
                ctx.PurchaseOrders.Remove(po);

                await ctx.SaveChangesAsync();
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
        public async Task<string> PostDirectBillAsync(VendorBill bill, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                if (bill.CompanyId == Guid.Empty) return "Company ID is missing.";
                if (bill.VendorId == Guid.Empty) return "Vendor is required.";
                if (!bill.Lines.Any()) return "Bill must have at least one line.";

                var vendor = await ctx.Vendors.FindAsync(bill.VendorId);
                if (vendor == null) return "Selected vendor does not exist.";

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
                    Guid lineExpenseAccount = line.ExpenseGlAccountId != Guid.Empty
                        ? line.ExpenseGlAccountId
                        : defaultExpenseAccount;

                    line.ExpenseGlAccountId = lineExpenseAccount;

                    decimal lineTotalForeign = line.QuantityBilled * line.UnitCostBilled;
                    totalGrossForeign += lineTotalForeign;

                    decimal lineTotalBase = Math.Round(lineTotalForeign * rate, 2);
                    grossBaseForLedger += lineTotalBase;

                    string glRef = !string.IsNullOrWhiteSpace(line.Description)
                        ? line.Description
                        : $"Direct Bill: {bill.ExternalInvoiceNumber}";

                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = lineExpenseAccount,
                        Debit = lineTotalBase,
                        Credit = 0,
                        Reference = glRef
                    });
                }

                // Process Tax (Debit Tax Asset / Input VAT)
                decimal taxForeign = 0;
                decimal taxBaseForLedger = 0;

                if (bill.TaxId.HasValue && bill.TaxGLAccountId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(bill.TaxId);
                    if (tax != null)
                    {
                        taxForeign = totalGrossForeign * (tax.Per / 100);
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
                bill.TotalAmountForeign = totalGrossForeign + taxForeign;
                decimal actualCreditBase = grossBaseForLedger + taxBaseForLedger;
                bill.TotalAmount = actualCreditBase;

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
                    line.VendorBillId = bill.Id;
                }

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    bill.CompanyId,
                    DateOnly.FromDateTime(bill.BillDate),
                    "Direct Vendor Bill",
                    $"Bill {bill.ExternalInvoiceNumber}",
                    glLines,
                    userId);

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(bill.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Engine Rejected Posting: {postErr}");
                }

                ctx.VendorBills.Add(bill);
                ctx.VendorBillLines.AddRange(bill.Lines);

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
        public async Task<List<VendorBill>> GetDirectBillsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.VendorBills
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
                    "Days Outstanding", "Order Value (Base)", "Status", "Aging Bucket"
                },
                Rows = new List<List<string>>()
            };

            decimal grandTotal = 0;

            foreach (var po in invoicedPOs)
            {
                string invoiceNumber = invoices[po.OrderNumber]; // Guaranteed to exist via filter
                decimal orderValue = po.Lines.Sum(l => l.QuantityOrdered * l.UnitCost) * po.ExchangeRate;
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
        // 2. GOODS RECEIPT NOTE (GRN) LOG (INVOICED ONLY)
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

            var poIds = grns.Select(g => g.PurchaseOrderId).Distinct().ToList();
            var poDict = await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => poIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            var poNumbers = poDict.Values.Select(p => p.OrderNumber).ToList();

            // Extract downstream mapped invoices
            var invoices = await ctx.PurchaseOrders
                .AsNoTracking()
                .Where(i => i.CompanyId == companyId
                         && !string.IsNullOrEmpty(i.ConvertedFromPONumber)
                         && poNumbers.Contains(i.ConvertedFromPONumber))
                .ToDictionaryAsync(i => i.ConvertedFromPONumber!, i => i.OrderNumber);

            var itemIds = poDict.Values
                .SelectMany(p => p.Lines)
                .Select(l => l.ItemId)
                .Distinct()
                .ToList();

            var items = await ctx.Items
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name);

            var vendorIds = poDict.Values.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Goods Receipt Note (GRN) Log (Invoiced Orders)",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "GRN Number", "Invoice Order Number", "Vendor", "Date Received",
                    "Item", "Qty Received", "Unit Cost", "Line Value (Base)"
                },
                Rows = new List<List<string>>()
            };

            decimal grandValue = 0;
            decimal grandQty = 0;

            foreach (var grn in grns)
            {
                var po = poDict.GetValueOrDefault(grn.PurchaseOrderId);
                if (po == null || !invoices.ContainsKey(po.OrderNumber)) continue; // FILTER OUT NON-INVOICED POs

                string invoiceNumber = invoices[po.OrderNumber];
                string vendorName = vendors.GetValueOrDefault(po.VendorId, "Unknown");

                foreach (var line in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                    decimal unitCost = poLine?.UnitCost ?? 0;
                    decimal rate = po.ExchangeRate;
                    decimal lineValue = Math.Round(line.QuantityReceived * unitCost * rate, 2);
                    string itemName = poLine != null ? items.GetValueOrDefault(poLine.ItemId, "Unknown") : "Unknown";

                    reportData.Rows.Add(new List<string>
                    {
                        grn.GrnNumber,
                        invoiceNumber, // Displays just the invoice order number
                        vendorName,
                        grn.DateReceived.ToString("MMM dd, yyyy"),
                        itemName,
                        line.QuantityReceived.ToString("N2"),
                        unitCost.ToString("N2"),
                        lineValue.ToString("N2")
                    });

                    grandValue += lineValue;
                    grandQty += line.QuantityReceived;
                }
            }

            reportData.Rows.Add(new List<string>
                { "GRAND TOTAL", "", "", "", "", grandQty.ToString("N2"), "", grandValue.ToString("N2") });

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
                .Where(l => poLineIds.Contains(l.PurchaseOrderLineId))
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
                .Where(l => poLineIds.Contains(l.PurchaseOrderLineId))
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
                    "Total Ordered", "Total Received", "Remaining", "% Received", "Order Value (Base)"
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
                    .Where(r => lineIds.Contains(r.PurchaseOrderLineId))
                    .Sum(r => r.QuantityReceived);

                decimal remaining = qtyOrdered - qtyReceived;
                decimal pct = qtyOrdered > 0 ? (qtyReceived / qtyOrdered) * 100 : 0;
                decimal orderValue = po.Lines.Sum(l => l.QuantityOrdered * l.UnitCost) * po.ExchangeRate;

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

            var billIds = allPayments.Select(p => p.VendorBillId).Distinct().ToList();

            // 2. Cross-reference against Posted Vendor Bills ONLY (drops all unposted bill drafts)
            var bills = await ctx.VendorBills
                .AsNoTracking()
                .Where(b => billIds.Contains(b.Id) && b.CompanyId == companyId && b.IsPosted == true)
                .ToDictionaryAsync(b => b.Id);

            var companyBillIds = bills.Keys.ToHashSet();

            // 3. FILTER: Keep payments only if they belong to a verified posted bill partition
            var postedPayments = allPayments
                .Where(p => companyBillIds.Contains(p.VendorBillId))
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
                    "Payment Reference", "Bank Account", "Amount Paid (Base)"
                },
                Rows = new List<List<string>>()
            };

            decimal grandTotal = 0;

            foreach (var payment in postedPayments)
            {
                var bill = bills.GetValueOrDefault(payment.VendorBillId);
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
            var bills = await ctx.VendorBills
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
                    "Total Billed (Base)", "Total Paid (Base)", "Outstanding Balance (Base)"
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
                    "Gross Value (Base)", "Discount Type", "Discount Amount (Base)", "Net Value (Base)"
                },
                Rows = new List<List<string>>()
            };

            decimal grandGross = 0;
            decimal grandDiscount = 0;

            foreach (var po in invoicedPOs)
            {
                string invoiceNumber = invoices[po.OrderNumber];
                decimal grossForeign = po.Lines.Sum(l => l.QuantityOrdered * l.UnitCost);
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

            var bills = await ctx.VendorBills
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
                    "Linked PO", "Bill Amount (Base)", "Variance Reason"
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
                var bill = await ctx.VendorBills
                    .Include(b => b.Payments)
                    .FirstOrDefaultAsync(b => b.Id == reversalPayment.VendorBillId && b.CompanyId == companyId);

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
    VendorBillId = bill.Id,
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
    userId
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