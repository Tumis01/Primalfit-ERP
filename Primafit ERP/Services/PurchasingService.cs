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

        public PurchasingService(
            IDbContextFactory<AppDbContext> dbFactory,
            InventoryService inventoryService,
            GLOperationsService glOps,
            BudgetService budgetService)
        {
            _dbFactory = dbFactory;
            _inventoryService = inventoryService;
            _glOps = glOps;
            _budgetService = budgetService;

        }



        // Services/PurchasingService.cs

        public async Task<string> SaveVendorBillAsync(VendorBill bill)
        {
            // FIX 1: Prevent Null Crashes
            if (bill.MatchVarianceReason == null) bill.MatchVarianceReason = "";
            if (bill.ExternalInvoiceNumber == null) bill.ExternalInvoiceNumber = "";
            // FIX 2: Validate Company ID (The most common cause of "Entity Save" errors)
            if (bill.CompanyId == Guid.Empty)
                return "System Error: Bill has no Company ID. Please log out and log in again.";

            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Basic Validation
            if (bill.Lines.Count == 0) return "Bill must have at least one line.";
            if (bill.VendorId == Guid.Empty) return "Vendor is required.";

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
                    ctx.VendorBills.Add(bill);     // add header first
                    ctx.VendorBillLines.AddRange(bill.Lines); // then lines

                }
                else
                {
                    // --- UPDATE EXISTING ---
                    bill.CompanyId = existing.CompanyId; // Preserve Company ID
                    ctx.Entry(existing).CurrentValues.SetValues(bill);

                    ctx.VendorBillLines.RemoveRange(existing.Lines);
                    foreach (var line in bill.Lines)
                    {
                        line.VendorBillId = bill.Id;
                        ctx.VendorBillLines.Add(line);
                    }
                }
                if (!await ctx.CompanyDetails.AnyAsync(c => c.CompanyDetailsId == bill.CompanyId))
                    return "STOP: Company does not exist.";

                if (!await ctx.Vendors.AnyAsync(v => v.Id == bill.VendorId))
                    return "STOP: Vendor does not exist.";

                if (bill.PurchaseOrderId.HasValue && !await ctx.PurchaseOrders.AnyAsync(p => p.Id == bill.PurchaseOrderId.Value))
                    return "STOP: Linked PO does not exist.";

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            // FIX 3: USE RECURSIVE ERROR LOGGING (To find the real reason)
            catch (Exception ex)
            {
                var msg = ex.Message;
                var inner = ex.InnerException;
                while (inner != null)
                {
                    msg += " --> " + inner.Message;
                    inner = inner.InnerException;
                }

                // This will now print "Foreign Key Constraint FK_VendorBills_Companies" instead of just "Error saving"
                return $"DATABASE ERROR: {msg}";
            }
        }

        // 2. THREE-WAY MATCH LOGIC
        // Compares: PO Price vs GRN Quantity vs Bill Amount
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

            // A. Get the PO and Receipts
            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value);

            if (po == null) return "Purchase Order not found.";

            var receipts = await ctx.GoodsReceipts
                .Include(g => g.Lines)
                .Where(g => g.PurchaseOrderId == bill.PurchaseOrderId.Value)
                .ToListAsync();

            // ✅ Calculate RECEIVED VALUE IN BASE CURRENCY
            decimal totalReceivedValueBase = 0;

            foreach (var grn in receipts)
            {
                foreach (var line in grn.Lines)
                {
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                    if (poLine != null)
                    {
                        // Vendor currency value
                        decimal receivedForeign = line.QuantityReceived * poLine.UnitCost;

                        // Convert to base using PO exchange rate (locked at PO time)
                        decimal receivedBase = receivedForeign * po.ExchangeRate;

                        totalReceivedValueBase += receivedBase;
                    }
                }
            }

            // B. Compare Bill (BASE) vs Received (BASE)
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

        public async Task<string> SavePurchaseOrderAsync(PurchaseOrder po)
        {
            // ... [Keep your Validation and Budget Checks here] ...

            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Ask the DB: "Do you know this ID?"
            var existing = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == po.Id);

            try
            {
                if (existing == null)
                {
                    // --- CASE A: NEW ORDER (Even if it has an ID) ---

                    // Ensure the main ID is valid
                    if (po.Id == Guid.Empty) po.Id = Guid.NewGuid();

                    // Ensure lines are linked correctly
                    foreach (var line in po.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.PurchaseOrderId = po.Id; // Link Line to Header
                        ctx.PurchaseOrderLines.Add(line); // Explicitly track line
                    }

                    ctx.PurchaseOrders.Add(po); // Explicitly track header
                }
                else
                {
                    // --- CASE B: UPDATE EXISTING ---

                    // Protect CompanyId integrity
                    po.CompanyId = existing.CompanyId;

                    // Update Header values
                    ctx.Entry(existing).CurrentValues.SetValues(po);

                    // Replace Lines (Clear old, add new)
                    ctx.PurchaseOrderLines.RemoveRange(existing.Lines);
                    foreach (var line in po.Lines)
                    {
                        line.PurchaseOrderId = po.Id; // Ensure Link
                        ctx.PurchaseOrderLines.Add(line);
                    }
                }

                // 2. Commit to Database
                int changes = await ctx.SaveChangesAsync();

                // OPTIONAL DEBUG: Check if SQL actually wrote something
                // if (changes == 0) return "WARNING: SQL reported 0 rows affected.";

                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                return $"DATABASE ERROR: {ex.Message}";
            }
        }

        // --- GOODS RECEIPT (THE BRIDGE TO INVENTORY) ---

        public async Task<string> SaveGoodsReceiptAsync(GoodsReceipt grn, Guid warehouseId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            try
            {
                if (grn.CompanyId == Guid.Empty) return "STOP: No CompanyId.";
                if (grn.PurchaseOrderId == Guid.Empty) return "STOP: No Purchase Order selected.";
                if (warehouseId == Guid.Empty) return "STOP: No Warehouse selected.";
                if (grn.Lines == null || grn.Lines.Count == 0) return "STOP: GRN has no lines.";

                var po = await ctx.PurchaseOrders
                    .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId && p.CompanyId == grn.CompanyId);

                if (po == null) return "STOP: PO not found.";
                if (po.IsInvoicePosted) return "STOP: Invoice already posted. Cannot receive again.";

                if (grn.Id == Guid.Empty) grn.Id = Guid.NewGuid();
                if (string.IsNullOrWhiteSpace(grn.GrnNumber))
                    grn.GrnNumber = $"GRN-{DateTime.Now:yyMM}-{Random.Shared.Next(100, 999)}";

                foreach (var line in grn.Lines)
                {
                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                    line.GoodsReceiptId = grn.Id;
                    if (line.QuantityReceived < 0) return "STOP: Negative qty not allowed.";
                }

                // ✅ Save GRN
                ctx.GoodsReceipts.Add(grn);
                foreach (var grnLine in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    var poLine = await ctx.PurchaseOrderLines
                        .FirstOrDefaultAsync(l => l.Id == grnLine.PurchaseOrderLineId);

                    if (poLine == null)
                        return "STOP: PO line not found for a GRN line.";

                    // Example StockLedger entry
                    ctx.StockLedgers.Add(new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = grn.CompanyId,
                        WarehouseId = warehouseId,
                        ItemId = poLine.ItemId,
                        Date = grn.DateReceived,
                        Reference = grn.GrnNumber,
                        Type = StockMovementType.Purchase,
                        QuantityChanged = grnLine.QuantityReceived
                    });

                    // OPTIONAL: update Item weighted average cost using your logic
                    // (If you maintain WeightedAverageCost on Item)
                }
                await ctx.SaveChangesAsync();

                // ✅ Mark PO as received (but keep Status OPEN)
                po.HasReceipt = true;

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                var inner = ex.InnerException;
                while (inner != null) { msg += " --> " + inner.Message; inner = inner.InnerException; }
                return $"DB ERROR: {msg}";
            }
        }


        public async Task<List<PurchaseOrder>> GetOpenPOsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            return await ctx.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId && p.Status != PurchaseOrderStatus.Closed) // <--- FILTER ADDED
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

            //  Get Vendor and their Currency
            var vendor = await ctx.Vendors
                .Include(v => v.DefaultCurrency) // Ensure you have a navigation property or Join manually
                .FirstOrDefaultAsync(v => v.Id == vendorId);

            if (vendor == null) return (Guid.Empty, "", 1);

            //  vendor has no specific currency, assume Company Base Currency (Rate 1)
            if (vendor.CurrencyId == Guid.Empty) return (Guid.Empty, "BASE", 1);

            //  Get Company Base Currency Code (for logic check)
            var company = await ctx.CompanyDetails.FindAsync(companyId);
            if (company == null) return (Guid.Empty, "", 1);

            //  Get Latest Exchange Rate
            // Logic: Find the most recent rate for this Currency linked to this Company
            var latestRateEntry = await ctx.CurrencyManagements
                .Where(c => c.CompanyId == companyId && c.CurrencyId == vendor.CurrencyId)
                .OrderByDescending(c => c.Date)
                .FirstOrDefaultAsync();

            decimal rate = latestRateEntry?.Rate ?? 1.0m; // Default to 1 if no rate found

           

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

            // 1. Load the Bill
            var bill = await ctx.VendorBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null) return "Bill not found.";

            // --- CHECK 1: VALIDATE ACCOUNTS (The most common cause) ---
            // Check AP Account
            if (bill.AccountsPayableGlId == Guid.Empty)
                return "STOP: The AP Account is not set on the Bill.";
            if (bill.IsPosted)
                return "STOP: This bill has already been posted.";

            var apAccountExists = await ctx.GLChartOfAccounts.AnyAsync(a => a.Id == bill.AccountsPayableGlId);
            if (!apAccountExists)
                return "STOP: The AP Account ID exists but the Account itself is deleted or missing from the Chart of Accounts.";

            // Check Expense Accounts
            foreach (var line in bill.Lines)
            {
                if (line.ExpenseGlAccountId == Guid.Empty)
                    return $"STOP: Line item '' has no Expense Account.";

                var expAccountExists = await ctx.GLChartOfAccounts.AnyAsync(a => a.Id == line.ExpenseGlAccountId);
                if (!expAccountExists)
                    return $"STOP: The Expense Account for '' is invalid/deleted.";
            }

            // --- CHECK 2: VALIDATE PERIOD (The second most common cause) ---
            // The GL Engine needs an open period for the Bill Date.
            DateOnly postDate = DateOnly.FromDateTime(bill.BillDate);
            var period = await ctx.AccountingPeriods
                .FirstOrDefaultAsync(p => p.CompanyId == bill.CompanyId
                                       && p.StartDate <= postDate
                                       && p.EndDate >= postDate);

            if (period == null)
                return $"STOP: No Accounting Period exists for {postDate}. Go to Configuration > Fiscal Periods and create it.";

            if (period.IsClosed)
                return $"STOP: The Accounting Period for {postDate} is Closed.";

            if (bill.TotalAmount == 0 && bill.TotalAmountForeign > 0 && bill.ExchangeRate > 0)
            {
                bill.TotalAmount = bill.TotalAmountForeign * bill.ExchangeRate;
            }

            // --- 3. PREPARE GL LINES (IN BASE CURRENCY) ---
            var glLines = new List<GLJournalLine>();
            var vendor = await ctx.Vendors.FindAsync(bill.VendorId);
            string vendorName = vendor?.Name ?? "Unknown";

            // Debits (Expenses/Assets) - Converted to Base
            foreach (var line in bill.Lines)
            {
                // Calculate Line Total in Base Currency
                // Formula: (Qty * UnitCostForeign) * ExchangeRate
                decimal lineTotalBase = (line.QuantityBilled * line.UnitCostBilled) * bill.ExchangeRate;

                glLines.Add(new GLJournalLine
                {
                    AccountId = line.ExpenseGlAccountId,
                    Debit = lineTotalBase, // <--- POSTING BASE AMOUNT
                    Credit = 0,
                    Reference = $"Bill: {bill.ExternalInvoiceNumber}"
                });
            }

            // Credit (Accounts Payable) - Converted to Base
            glLines.Add(new GLJournalLine
            {
                AccountId = bill.AccountsPayableGlId,
                Debit = 0,
                Credit = bill.TotalAmount, // <--- POSTING BASE AMOUNT
                Reference = $"Inv #{bill.ExternalInvoiceNumber} - {vendorName}"
            });

            

            // Debits
            //foreach (var line in bill.Lines)
            //{
            //    glLines.Add(new GLJournalLine
            //    {
            //        AccountId = line.ExpenseGlAccountId,
            //        Debit = line.LineTotal,
            //        Credit = 0,
            //        Reference = $"Bill: "
            //    });
            //}

            //// Credit
            //glLines.Add(new GLJournalLine
            //{
            //    AccountId = bill.AccountsPayableGlId,
            //    Debit = 0,
            //    Credit = bill.TotalAmount,
            //    Reference = $"Inv #{bill.ExternalInvoiceNumber} - {vendorName}"
            //});

            try
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    bill.CompanyId,
                    postDate,
                    "Vendor Bill",
                    $"Inv #{bill.ExternalInvoiceNumber ?? "REF"}",
                    glLines
                );

                if (!string.IsNullOrEmpty(err)) return $"GL ENGINE ERROR: {err}";

                if (batchId.HasValue)
                {
                    await _glOps.PostBatchAsync(bill.CompanyId, batchId.Value);
                }

                // Final Save
                if (bill.ExternalInvoiceNumber == null) bill.ExternalInvoiceNumber = "N/A";
                if (bill.MatchVarianceReason == null) bill.MatchVarianceReason = "";
                if (bill.PurchaseOrderId != null)
                {
                    var po = await ctx.PurchaseOrders
                        .FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value && p.CompanyId == bill.CompanyId);

                    if (po != null)
                    {
                        po.IsInvoicePosted = true;
                        po.Status = PurchaseOrderStatus.Closed; // or Posted, depending on your enum naming
                    }
                }
                bill.IsPosted = true;
                bill.PostedDate = DateTime.Now;

                if (bill.PurchaseOrderId.HasValue)
                {
                    var po = await ctx.PurchaseOrders
                        .FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value && p.CompanyId == bill.CompanyId);

                    if (po != null)
                    {
                        po.IsInvoicePosted = true;           // from our redesign
                        po.Status = PurchaseOrderStatus.Closed; // or Posted
                    }
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                // Recursive Error Unwrapper
                var msg = ex.Message;
                var inner = ex.InnerException;
                while (inner != null)
                {
                    msg += " --> " + inner.Message;
                    inner = inner.InnerException;
                }
                return $"CRITICAL DB ERROR: {msg}";
            }

        }
    }
}