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

        public async Task<string> AutoPostVendorBillFromPOAsync(Guid poId, Guid companyId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var po = await ctx.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == poId);
                if (po == null) return "PO not found.";
                if (po.IsInvoicePosted) return "Invoice already posted.";

                // We still check for at least ONE GRN just to extract the GR/IR account routing
                var grns = await ctx.GoodsReceipts.Include(g => g.Lines).Where(g => g.PurchaseOrderId == poId).ToListAsync();
                if (!grns.Any()) return "No Goods Receipt found. At least one receipt is required to establish the GR/IR routing.";

                var grIrAccountId = grns.First().InventoryGlAccountId;
                if (grIrAccountId == Guid.Empty) return "GR/IR Clearing Account was not set on the Goods Receipt.";

                var vendor = await ctx.Vendors.FindAsync(po.VendorId);
                if (vendor?.PayablesAccountId == null) return "Vendor Payables account missing in Master Data.";

                var bill = new VendorBill
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    VendorId = po.VendorId,
                    PurchaseOrderId = po.Id,
                    AccountsPayableGlId = vendor.PayablesAccountId.Value,
                    ExternalInvoiceNumber = $"INV-{po.OrderNumber}",
                    BillDate = DateTime.Today,
                    CurrencyId = po.CurrencyId,
                    ExchangeRate = po.ExchangeRate,
                    IsPosted = true,
                    PostedDate = DateTime.Now,
                    MatchStatus = BillMatchStatus.Matched
                };

                decimal totalGrossForeign = 0;

                foreach (var poLine in po.Lines)
                {
                    if (poLine.QuantityOrdered > 0)
                    {
                        bill.Lines.Add(new VendorBillLine
                        {
                            Id = Guid.NewGuid(),
                            VendorBillId = bill.Id,
                            ItemId = poLine.ItemId,
                            QuantityBilled = poLine.QuantityOrdered, // Always bill the FULL ordered amount
                            UnitCostBilled = poLine.UnitCost,
                            ExpenseGlAccountId = grIrAccountId
                        });
                        totalGrossForeign += (poLine.QuantityOrdered * poLine.UnitCost);
                    }
                }

                // --- APPLY DISCOUNT & TAX MATH ---
                decimal discountForeign = po.DiscountAmount;
                if (po.DiscountPercentage > 0)
                {
                    discountForeign = totalGrossForeign * (po.DiscountPercentage / 100);
                }
                decimal netForeign = totalGrossForeign - discountForeign;

                decimal taxForeign = 0;
                if (po.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(po.TaxId);
                    if (tax != null) taxForeign = netForeign * (tax.Per / 100);
                }

                bill.TotalAmountForeign = netForeign + taxForeign;

                // Convert to Base Ledger Currency
                decimal grossBase = Math.Round(totalGrossForeign * po.ExchangeRate, 2);
                decimal discountBase = Math.Round(discountForeign * po.ExchangeRate, 2);
                decimal taxBase = Math.Round(taxForeign * po.ExchangeRate, 2);
                decimal grandTotalBase = (grossBase - discountBase) + taxBase;

                bill.TotalAmount = grandTotalBase;

                ctx.VendorBills.Add(bill);
                ctx.VendorBillLines.AddRange(bill.Lines);

                // --- CREATE GL JOURNAL ---
                var glLines = new List<GLJournalLine>
                {
                    // 1. DEBIT: GR/IR (Clearing the full PO liability)
                    new GLJournalLine { SegCoaId = grIrAccountId, Debit = grossBase, Credit = 0, Reference = $"Clear GR/IR: {bill.ExternalInvoiceNumber}" }
                };

                // 2. DEBIT: Input Tax (Asset/Receivable from Govt)
                if (taxBase > 0 && po.TaxGLAccountId.HasValue)
                {
                    glLines.Add(new GLJournalLine { SegCoaId = po.TaxGLAccountId.Value, Debit = taxBase, Credit = 0, Reference = $"Input Tax: {bill.ExternalInvoiceNumber}" });
                }

                // 3. CREDIT: Discount Received (Income / Contra-Expense)
                if (discountBase > 0)
                {
                    // --- THE FIX: INTERCEPT DISCOUNT RECEIVED ---
                    Guid discountAccount = await _mappingService.GetMappedAccountAsync(
                        companyId,
                        SystemTransactionType.DiscountReceived,
                        isDebit: false, // Discount Received is a Credit/Income
                        defaultAccountId: po.DiscountGlAccountId ?? Guid.Empty);

                    if (discountAccount == Guid.Empty) return "A discount was applied, but no Discount Received GL Account is mapped.";

                    glLines.Add(new GLJournalLine { SegCoaId = discountAccount, Debit = 0, Credit = discountBase, Reference = $"Discount Received: {bill.ExternalInvoiceNumber}" });
                }

                // 4. CREDIT: Accounts Payable (The actual net amount we owe the vendor)
                // --- THE FIX: INTERCEPT ACCOUNTS PAYABLE ---
                Guid apAccount = await _mappingService.GetMappedAccountAsync(
                    companyId,
                    SystemTransactionType.PurchaseInvoice,
                    isDebit: false, // AP is a Credit
                    defaultAccountId: bill.AccountsPayableGlId);

                glLines.Add(new GLJournalLine { SegCoaId = apAccount, Debit = 0, Credit = grandTotalBase, Reference = $"Vendor Bill: {bill.ExternalInvoiceNumber}" });

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(bill.BillDate), "Vendor Bill Auto-Post", $"Inv {bill.ExternalInvoiceNumber}", glLines, userId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value, userId);

                po.IsInvoicePosted = true;

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Error posting invoice: {ex.Message}";
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
                if (grn.InventoryGlAccountId == Guid.Empty) return "STOP: You must select a GR/IR Clearing Account.";

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

                ctx.GoodsReceipts.Add(grn);

                var glLines = new List<GLJournalLine>();
                decimal totalReceivedValueBase = 0;

                // Post Inventory & Financials
                foreach (var grnLine in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == grnLine.PurchaseOrderLineId);
                    var item = await ctx.Items.FindAsync(poLine.ItemId);

                    decimal lineValueForeign = grnLine.QuantityReceived * poLine.UnitCost;
                    decimal lineValueBase = Math.Round(lineValueForeign * po.ExchangeRate, 2);
                    totalReceivedValueBase += lineValueBase;

                    if (!item.IsService)
                    {
                        // 1. Physical Stock Increase
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

                        // 2. Debit: Inventory Asset
                        if (item.InventoryAssetAccountId != Guid.Empty)
                        {
                            glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = lineValueBase, Credit = 0, Reference = $"GRN Recv: {item.Name}" });
                        }
                    }
                    else if (item.CostOfGoodsSoldAccountId != Guid.Empty)
                    {
                        // Debit: Expense (for Services)
                        glLines.Add(new GLJournalLine { SegCoaId = item.CostOfGoodsSoldAccountId, Debit = lineValueBase, Credit = 0, Reference = $"Service Recv: {item.Name}" });
                    }
                }

                // 3. CREDIT: GR/IR CLEARING ACCOUNT (Temporary Liability)
                if (glLines.Any())
                {
                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = grn.InventoryGlAccountId,
                        Debit = 0,
                        Credit = totalReceivedValueBase,
                        Reference = $"GR/IR Accrual for {grn.GrnNumber}"
                    });

                    var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(grn.CompanyId, DateOnly.FromDateTime(grn.DateReceived), "Goods Receipt", $"GRN {grn.GrnNumber}", glLines, userId);
                    if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                    if (batchId.HasValue) await _glOps.PostBatchAsync(grn.CompanyId, batchId.Value, userId);
                }

                // Update PO Status Tracking
                po.HasReceipt = true;
                decimal totalOrdered = po.Lines.Sum(l => l.QuantityOrdered);
                decimal totalCurrentlyReceiving = grn.Lines.Sum(l => l.QuantityReceived);
                decimal totalPastReceived = pastReceipts.Sum(l => l.QuantityReceived);

                if ((totalPastReceived + totalCurrentlyReceiving) >= totalOrdered) po.IsFullyReceived = true;
                po.Status = PurchaseOrderStatus.PartiallyReceived;

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
            if (bill.AccountsPayableGlId == Guid.Empty) return "STOP: The AP Account is not set.";

            // Validate Accounts in SegCOA
            bool apExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == bill.AccountsPayableGlId && a.CompanyId == bill.CompanyId && a.IsActive);
            if (!apExists) return "STOP: AP Account ID is invalid or Inactive.";

            foreach (var line in bill.Lines)
            {
                if (line.ExpenseGlAccountId == Guid.Empty) return "STOP: Line missing Expense Account.";
                bool expExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == line.ExpenseGlAccountId && a.CompanyId == bill.CompanyId && a.IsActive);
                if (!expExists) return "STOP: Selected Expense/Asset account is invalid or Inactive.";
            }

            // Validate Period
            DateOnly postDate = DateOnly.FromDateTime(bill.BillDate);
            var period = await ctx.AccountingPeriods
                .FirstOrDefaultAsync(p => p.CompanyId == bill.CompanyId && p.StartDate <= postDate && p.EndDate >= postDate);

            if (period == null || period.IsClosed) return $"STOP: No Open Period for {postDate}.";

            var glLines = new List<GLJournalLine>();
            var vendorName = (await ctx.Vendors.FindAsync(bill.VendorId))?.Name ?? "Unknown";

            decimal totalDebitsBase = 0;

            // 1. DEBITS (Expense/Asset Lines)
            foreach (var line in bill.Lines)
            {
                decimal lineTotalBase = Math.Round((line.QuantityBilled * line.UnitCostBilled) * bill.ExchangeRate, 2);
                totalDebitsBase += lineTotalBase;

                // NEW: Use the individual line Description if available!
                string glRef = !string.IsNullOrWhiteSpace(line.Description) ? line.Description : $"Bill: {bill.ExternalInvoiceNumber}";

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = line.ExpenseGlAccountId,
                    Debit = lineTotalBase,
                    Credit = 0,
                    Reference = glRef
                });
            }

            // 2. DEBIT TAX ASSET (If Applicable)
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

            // 3. CREDIT AP LIABILITY
            // Force the exact debit sum into the TotalAmount to prevent rounding fraction crashes in the GL Engine
            bill.TotalAmount = totalDebitsBase;

            glLines.Add(new GLJournalLine
            {
                SegCoaId = bill.AccountsPayableGlId,
                Debit = 0,
                Credit = totalDebitsBase,
                Reference = $"Inv #{bill.ExternalInvoiceNumber} - {vendorName}"
            });

            // Post to GL
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

            // Update Status
            bill.IsPosted = true;
            bill.PostedDate = DateTime.Now;

            // --- UPDATE PO ---
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
                var bill = await ctx.VendorBills.Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == payment.VendorBillId);
                if (bill == null) return "Bill not found.";
                if (!bill.IsPosted) return "Cannot pay an unposted bill.";
                if (payment.Amount <= 0) return "Payment amount must be > 0.";

                decimal currentPaid = bill.Payments.Sum(p => p.Amount);
                if (currentPaid + payment.Amount > bill.TotalAmount)
                    return $"Payment of {payment.Amount:N2} exceeds remaining balance of {(bill.TotalAmount - currentPaid):N2}.";

                if (payment.Id == Guid.Empty) payment.Id = Guid.NewGuid();
                ctx.Set<VendorPayment>().Add(payment);

                var glLines = new List<GLJournalLine>
                {
                    new GLJournalLine { SegCoaId = bill.AccountsPayableGlId, Debit = payment.Amount, Credit = 0, Reference = $"Pay: {bill.ExternalInvoiceNumber}" },
                    new GLJournalLine { SegCoaId = payment.BankGlAccountId, Debit = 0, Credit = payment.Amount, Reference = $"Pay: {bill.ExternalInvoiceNumber}" }
                };

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(payment.Date), "Vendor Payment", payment.Reference, glLines, userId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value, userId);

                // --- CLOSE PO ONLY IF FULLY PAID ---
                if (bill.PurchaseOrderId.HasValue && (currentPaid + payment.Amount) >= bill.TotalAmount)
                {
                    var po = await ctx.PurchaseOrders.FindAsync(bill.PurchaseOrderId.Value);
                    if (po != null)
                    {
                        po.IsFullyPaid = true;
                        po.Status = PurchaseOrderStatus.Closed; // Marks as officially closed!
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
                if (bill.AccountsPayableGlId == Guid.Empty) return "Accounts Payable GL Account is required.";
                if (!bill.Lines.Any()) return "Bill must have at least one line.";

                var vendor = await ctx.Vendors.FindAsync(bill.VendorId);
                if (vendor == null) return "Selected vendor does not exist.";

                if (string.IsNullOrWhiteSpace(bill.ExternalInvoiceNumber))
                    bill.ExternalInvoiceNumber = $"INV-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";

                decimal rate = bill.ExchangeRate > 0 ? bill.ExchangeRate : 1;
                decimal totalGrossForeign = 0;
                decimal grossBaseForLedger = 0;
                var glLines = new List<GLJournalLine>();

                // 1. Process Lines (DEBIT EXPENSES)
                foreach (var line in bill.Lines)
                {
                    // --- THE FIX: We pass Guid.Empty because the UI no longer provides it ---
                    Guid expenseAccount = await _mappingService.GetMappedAccountAsync(
                        bill.CompanyId,
                        SystemTransactionType.DirectPurchaseInvoice,
                        isDebit: true, // Expenses are Debits
                        defaultAccountId: Guid.Empty);

                    if (expenseAccount == Guid.Empty) return "An Expense GL Account is required for Direct Bills. Please configure it in GL Mapping Settings.";

                    decimal lineTotalForeign = line.QuantityBilled * line.UnitCostBilled;
                    totalGrossForeign += lineTotalForeign;

                    decimal lineTotalBase = Math.Round(lineTotalForeign * rate, 2);
                    grossBaseForLedger += lineTotalBase;

                    string glRef = !string.IsNullOrWhiteSpace(line.Description)
                        ? line.Description
                        : $"Direct Bill: {bill.ExternalInvoiceNumber ?? "Expense"}";

                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = expenseAccount,
                        Debit = lineTotalBase,
                        Credit = 0,
                        Reference = glRef
                    });
                }

                // 2. Process Tax (DEBIT TAX ASSET)
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

                // 3. Accounts Payable (CREDIT AP LIABILITY)
                bill.TotalAmountForeign = totalGrossForeign + taxForeign;

                decimal actualCreditBase = grossBaseForLedger + taxBaseForLedger;
                bill.TotalAmount = actualCreditBase;

                // --- THE FIX: INTERCEPT ACCOUNTS PAYABLE FOR DIRECT BILLS ---
                Guid apAccount = await _mappingService.GetMappedAccountAsync(
                    bill.CompanyId,
                    SystemTransactionType.DirectPurchaseInvoice,
                    isDebit: false, // AP is a Credit
                    defaultAccountId: bill.AccountsPayableGlId);

                glLines.Add(new GLJournalLine
                {
                    SegCoaId = apAccount,
                    Debit = 0,
                    Credit = actualCreditBase,
                    Reference = $"Vendor Bill: {bill.ExternalInvoiceNumber}"
                });

                // Generate IDs and finalize status
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

                // POST GL BATCH
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(bill.CompanyId, DateOnly.FromDateTime(bill.BillDate), "Direct Vendor Bill", $"Bill {bill.ExternalInvoiceNumber}", glLines, userId);
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
        // 1. PO AGING REPORT
        //    Shows all open POs bucketed by how long they've been outstanding.
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GeneratePOAgingReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            var pos = await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId
                         && p.Status != PurchaseOrderStatus.Closed
                         && p.OrderDate >= startDt && p.OrderDate <= endDt)
                .OrderBy(p => p.OrderDate)
                .ToListAsync();

            var vendorIds = pos.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var today = DateOnly.FromDateTime(DateTime.Today);

            var reportData = new StandardReportData
            {
                ReportName = "Purchase Order Aging Report",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "PO Number", "Vendor", "Order Date",
                    "Days Outstanding", "Order Value (Base)", "Status", "Aging Bucket"
                },
                Rows = new List<List<string>>()
            };

            decimal grandTotal = 0;

            foreach (var po in pos)
            {
                decimal orderValue = po.Lines.Sum(l => l.QuantityOrdered * l.UnitCost) * po.ExchangeRate;
                int days = today.DayNumber - DateOnly.FromDateTime(po.OrderDate.Date).DayNumber;

                string bucket = days <= 30 ? "Current (0–30 Days)"
                              : days <= 60 ? "31–60 Days"
                              : days <= 90 ? "61–90 Days"
                              : "Over 90 Days";

                reportData.Rows.Add(new List<string>
                {
                    po.OrderNumber,
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
        //    Line-by-line log of all goods received within the date range.
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
                ReportName = "Goods Receipt Note (GRN) Log",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "GRN Number", "PO Number", "Vendor", "Date Received",
                    "Item", "Qty Received", "Unit Cost", "Line Value (Base)"
                },
                Rows = new List<List<string>>()
            };

            decimal grandValue = 0;
            decimal grandQty = 0;

            foreach (var grn in grns)
            {
                var po = poDict.GetValueOrDefault(grn.PurchaseOrderId);
                string vendorName = po != null ? vendors.GetValueOrDefault(po.VendorId, "Unknown") : "Unknown";

                foreach (var line in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    var poLine = po?.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                    decimal unitCost = poLine?.UnitCost ?? 0;
                    decimal rate = po?.ExchangeRate ?? 1;
                    decimal lineValue = Math.Round(line.QuantityReceived * unitCost * rate, 2);
                    string itemName = poLine != null ? items.GetValueOrDefault(poLine.ItemId, "Unknown") : "Unknown";

                    reportData.Rows.Add(new List<string>
                    {
                        grn.GrnNumber,
                        po?.OrderNumber ?? "N/A",
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
        // 3. OVER / UNDER RECEIVING REPORT
        //    Compares qty ordered vs qty received for each PO line.
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

            var poLineIds = pos.SelectMany(p => p.Lines).Select(l => l.Id).ToList();

            var receivedLines = await ctx.GoodsReceiptLines
                .AsNoTracking()
                .Where(l => poLineIds.Contains(l.PurchaseOrderLineId))
                .ToListAsync();

            var itemIds = pos.SelectMany(p => p.Lines).Select(l => l.ItemId).Distinct().ToList();
            var items = await ctx.Items
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name);

            var vendorIds = pos.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Over / Under Receiving Report",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "PO Number", "Vendor", "Item",
                    "Qty Ordered", "Qty Received", "Variance", "Status"
                },
                Rows = new List<List<string>>()
            };

            decimal totalOrdered = 0;
            decimal totalReceived = 0;

            foreach (var po in pos.OrderByDescending(p => p.OrderDate))
            {
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
                        po.OrderNumber,
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
        // 4. PARTIALLY RECEIVED POs
        //    Lists POs that have at least one receipt but are not yet
        //    fully received, showing how much stock is still outstanding.
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

            var poLineIds = pos.SelectMany(p => p.Lines).Select(l => l.Id).ToList();

            var receivedLines = await ctx.GoodsReceiptLines
                .AsNoTracking()
                .Where(l => poLineIds.Contains(l.PurchaseOrderLineId))
                .ToListAsync();

            var vendorIds = pos.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Partially Received Purchase Orders",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "PO Number", "Vendor", "Order Date",
                    "Total Ordered", "Total Received", "Remaining", "% Received", "Order Value (Base)"
                },
                Rows = new List<List<string>>()
            };

            decimal grandOrderValue = 0;

            foreach (var po in pos)
            {
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
                    po.OrderNumber,
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
        //    All payments made to vendors within the period, with bank
        //    account and bill reference traceability.
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GenerateVendorPaymentHistoryReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            // Load all payments in the date window
            var allPayments = await ctx.Set<VendorPayment>()
                .AsNoTracking()
                .Where(p => p.Date >= startDt && p.Date <= endDt)
                .OrderByDescending(p => p.Date)
                .ToListAsync();

            var billIds = allPayments.Select(p => p.VendorBillId).Distinct().ToList();

            // Filter to this company via the bill's CompanyId
            var bills = await ctx.VendorBills
                .AsNoTracking()
                .Where(b => billIds.Contains(b.Id) && b.CompanyId == companyId)
                .ToDictionaryAsync(b => b.Id);

            var companyBillIds = bills.Keys.ToHashSet();
            var payments = allPayments.Where(p => companyBillIds.Contains(p.VendorBillId)).ToList();

            var vendorIds = bills.Values.Select(b => b.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var glIds = payments.Select(p => p.BankGlAccountId).Distinct().ToList();
            var glAccounts = await ctx.SegChartOfAccounts
                .Where(a => glIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Description);

            var reportData = new StandardReportData
            {
                ReportName = "Vendor Payment History",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Payment Date", "Vendor", "Bill Reference",
                    "Payment Reference", "Bank Account", "Amount Paid (Base)"
                },
                Rows = new List<List<string>>()
            };

            decimal grandTotal = 0;

            foreach (var payment in payments)
            {
                var bill = bills.GetValueOrDefault(payment.VendorBillId);
                string vendorName = bill != null ? vendors.GetValueOrDefault(bill.VendorId, "Unknown") : "Unknown";
                string bankAcct = glAccounts.GetValueOrDefault(payment.BankGlAccountId, "Unknown Account");

                reportData.Rows.Add(new List<string>
                {
                    payment.Date.ToString("MMM dd, yyyy"),
                    vendorName,
                    bill?.ExternalInvoiceNumber ?? "N/A",
                    payment.Reference ?? "",
                    bankAcct,
                    payment.Amount.ToString("N2")
                });

                grandTotal += payment.Amount;
            }

            reportData.Rows.Add(new List<string>
                { "", "GRAND TOTAL", "", "", "", grandTotal.ToString("N2") });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 6. PURCHASE SPEND BY VENDOR
        //    Summarises total billed, total paid, and outstanding balance
        //    per vendor — great for AP exposure visibility.
        // ─────────────────────────────────────────────────────────────────
        public async Task<StandardReportData> GeneratePurchaseSpendByVendorReportAsync(Guid companyId, DateOnly start, DateOnly end)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var startDt = start.ToDateTime(TimeOnly.MinValue);
            var endDt = end.ToDateTime(TimeOnly.MaxValue);

            var bills = await ctx.VendorBills
                .AsNoTracking()
                .Include(b => b.Payments)
                .Where(b => b.CompanyId == companyId
                         && b.IsPosted == true
                         && b.BillDate >= startDt && b.BillDate <= endDt)
                .ToListAsync();

            var vendorIds = bills.Select(b => b.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Purchase Spend by Vendor",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "Vendor", "No. of Bills",
                    "Total Billed (Base)", "Total Paid (Base)", "Outstanding Balance (Base)"
                },
                Rows = new List<List<string>>()
            };

            var grouped = bills
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

                reportData.Rows.Add(new List<string>
                {
                    vendors.GetValueOrDefault(row.VendorId, "Unknown"),
                    row.BillCount.ToString("N0"),
                    row.TotalBilled.ToString("N2"),
                    row.TotalPaid.ToString("N2"),
                    outstanding.ToString("N2")
                });

                grandBilled += row.TotalBilled;
                grandPaid += row.TotalPaid;
            }

            reportData.Rows.Add(new List<string>
            {
                "GRAND TOTAL", "",
                grandBilled.ToString("N2"),
                grandPaid.ToString("N2"),
                (grandBilled - grandPaid).ToString("N2")
            });

            return reportData;
        }

        // ─────────────────────────────────────────────────────────────────
        // 7. DISCOUNT RECEIVED SUMMARY
        //    Reports every PO where a discount was applied, showing gross,
        //    discount amount, and net value — feeds directly to GL reconcile.
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

            var vendorIds = pos.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await ctx.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            var reportData = new StandardReportData
            {
                ReportName = "Discount Received Summary",
                ReportingPeriod = $"{start:MMM dd, yyyy} to {end:MMM dd, yyyy}",
                Headers = new List<string>
                {
                    "PO Number", "Vendor", "Order Date",
                    "Gross Value (Base)", "Discount Type", "Discount Amount (Base)", "Net Value (Base)"
                },
                Rows = new List<List<string>>()
            };

            decimal grandGross = 0;
            decimal grandDiscount = 0;

            foreach (var po in pos)
            {
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
                    po.OrderNumber,
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


    }
}