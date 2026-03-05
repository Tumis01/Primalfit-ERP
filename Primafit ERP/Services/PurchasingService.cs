using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
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
        public PurchasingService(
            IDbContextFactory<AppDbContext> dbFactory,
        InventoryService inventoryService,
        GLOperationsService glOps,
        BudgetService budgetService,
        InventoryValuationService valuationService)
        {
            _dbFactory = dbFactory;
            _inventoryService = inventoryService;
            _glOps = glOps;
            _budgetService = budgetService;
            _valuationService = valuationService;

        }





        public async Task<string> SaveVendorBillAsync(VendorBill bill)
        {
            if (bill.MatchVarianceReason == null) bill.MatchVarianceReason = "";
            if (bill.ExternalInvoiceNumber == null) bill.ExternalInvoiceNumber = "";

            if (bill.CompanyId == Guid.Empty) return "System Error: Bill has no Company ID.";
            if (bill.VendorId == Guid.Empty) return "Vendor is required.";
            if (bill.Lines == null || bill.Lines.Count == 0) return "Bill must have at least one line.";

            using var ctx = await _dbFactory.CreateDbContextAsync();

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

            if ((po.DiscountAmount > 0 || po.DiscountPercentage > 0) && po.DiscountGlAccountId == null)
            {
                return "You must select a Discount GL Account (Income/Credit) to apply a discount.";
            }

            var existing = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == po.Id);

            try
            {
                if (existing == null)
                {
                    if (po.Id == Guid.Empty) po.Id = Guid.NewGuid();

                    // --- FIX: GENERATE PO NUMBER IF IT'S BLANK ---
                    if (string.IsNullOrWhiteSpace(po.OrderNumber))
                    {
                        po.OrderNumber = $"PO-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";
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

                    po.CompanyId = existing.CompanyId;

                    // --- FIX: PRESERVE EXISTING PO NUMBER ON EDIT ---
                    po.OrderNumber = existing.OrderNumber;

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

        // 2. AUTO-POST VENDOR BILL (With Tax & Discount Math)
        public async Task<string> AutoPostVendorBillFromPOAsync(Guid poId, Guid companyId)
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

                // --- THE FIX: ITERATE OVER THE FULL PO LINES INSTEAD OF PARTIAL GRN LINES ---
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
                if (discountBase > 0 && po.DiscountGlAccountId.HasValue)
                {
                    glLines.Add(new GLJournalLine { SegCoaId = po.DiscountGlAccountId.Value, Debit = 0, Credit = discountBase, Reference = $"Discount Received: {bill.ExternalInvoiceNumber}" });
                }

                // 4. CREDIT: Accounts Payable (The actual net amount we owe the vendor)
                glLines.Add(new GLJournalLine { SegCoaId = bill.AccountsPayableGlId, Debit = 0, Credit = grandTotalBase, Reference = $"Vendor Bill: {bill.ExternalInvoiceNumber}" });

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(bill.BillDate), "Vendor Bill Auto-Post", $"Inv {bill.ExternalInvoiceNumber}", glLines);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value);

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

        public async Task<string> SaveGoodsReceiptAsync(GoodsReceipt grn, Guid warehouseId)
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

                    var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(grn.CompanyId, DateOnly.FromDateTime(grn.DateReceived), "Goods Receipt", $"GRN {grn.GrnNumber}", glLines);
                    if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                    if (batchId.HasValue) await _glOps.PostBatchAsync(grn.CompanyId, batchId.Value);
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
        public async Task<string> PostVendorBillAsync(Guid billId)
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

            // Prepare GL Lines
            var glLines = new List<GLJournalLine>();
            var vendorName = (await ctx.Vendors.FindAsync(bill.VendorId))?.Name ?? "Unknown";

            // DEBITS (Expense/Asset)
            foreach (var line in bill.Lines)
            {
                decimal lineTotalBase = (line.QuantityBilled * line.UnitCostBilled) * bill.ExchangeRate;
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = line.ExpenseGlAccountId,
                    Debit = lineTotalBase,
                    Credit = 0,
                    Reference = $"Bill: {bill.ExternalInvoiceNumber}"
                });
            }

            // CREDIT (AP Liability)
            glLines.Add(new GLJournalLine
            {
                SegCoaId = bill.AccountsPayableGlId,
                Debit = 0,
                Credit = bill.TotalAmount,
                Reference = $"Inv #{bill.ExternalInvoiceNumber} - {vendorName}"
            });

            // Post to GL
            var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                bill.CompanyId, postDate, "Vendor Bill",
                $"Inv #{bill.ExternalInvoiceNumber ?? "REF"}", glLines
            );

            if (!string.IsNullOrEmpty(err)) return $"GL ERROR: {err}";

            if (batchId.HasValue) await _glOps.PostBatchAsync(bill.CompanyId, batchId.Value);

            // Update Status
            bill.IsPosted = true;
            bill.PostedDate = DateTime.Now;

            // --- UPDATE PO (But DO NOT close it until paid) ---
            if (bill.PurchaseOrderId.HasValue)
            {
                var po = await ctx.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value);
                if (po != null) po.IsInvoicePosted = true;
            }
        

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<string> PostVendorPaymentAsync(VendorPayment payment, Guid companyId)
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

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(payment.Date), "Vendor Payment", payment.Reference, glLines);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value);

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
    }
}