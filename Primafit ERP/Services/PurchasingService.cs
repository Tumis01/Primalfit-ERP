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
            if (bill.MatchVarianceReason == null) bill.MatchVarianceReason = string.Empty;

            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Basic Validation
            if (bill.Lines.Count == 0) return "Bill must have at least one line.";
            if (bill.VendorId == Guid.Empty) return "Vendor is required.";
            if (bill.AccountsPayableGlId == Guid.Empty) return "AP Account is required.";

            // 2. CHECK DATABASE (The Fix)
            var existing = await ctx.VendorBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == bill.Id);

            try
            {
                if (existing == null)
                {
                    // --- NEW BILL ---
                    if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();

                    // Link lines
                    foreach (var line in bill.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.VendorBillId = bill.Id;
                        ctx.VendorBillLines.Add(line);
                    }

                    ctx.VendorBills.Add(bill);
                }
                else
                {
                    // --- UPDATE EXISTING ---
                    // Preserve CompanyId
                    bill.CompanyId = existing.CompanyId;

                    ctx.Entry(existing).CurrentValues.SetValues(bill);

                    // Replace lines
                    ctx.VendorBillLines.RemoveRange(existing.Lines);
                    foreach (var line in bill.Lines)
                    {
                        line.VendorBillId = bill.Id;
                        ctx.VendorBillLines.Add(line);
                    }
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (DbUpdateException dbEx)
            {
                // UNWRAP THE INNER EXCEPTION
                var errorMsg = dbEx.InnerException?.Message ?? dbEx.Message;

                // Check for specific common errors
                if (errorMsg.Contains("FOREIGN KEY constraint"))
                {
                    if (errorMsg.Contains("Vendors")) return "Database Error: Invalid Vendor ID.";
                    if (errorMsg.Contains("GLAccounts")) return "Database Error: One of the GL Accounts (AP or Expense) is missing or invalid.";
                }

                return $"Database Validation Error: {errorMsg}";
            }
            catch (Exception ex)
            {
                return $"System Error: {ex.Message}";
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
                return string.Empty; // Direct expenses don't need matching
            }

            // A. Get the PO and Receipts
            var po = await ctx.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId);

            // Get Total Value of Goods Received for this PO
            var receipts = await ctx.GoodsReceipts
                .Include(g => g.Lines)
                .Where(g => g.PurchaseOrderId == bill.PurchaseOrderId)
                .ToListAsync();

            // Calculate "Expected" Value based on what we physically received
            decimal totalReceivedValue = 0;

            foreach (var grn in receipts)
            {
                foreach (var line in grn.Lines)
                {
                    // Find original cost from PO
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                    if (poLine != null)
                    {
                        totalReceivedValue += (line.QuantityReceived * poLine.UnitCost);
                    }
                }
            }

            // B. Compare Bill vs Received
            // We allow a small tolerance (e.g. $1.00 or 1%) for rounding diffs
            decimal tolerance = 1.00m;
            decimal variance = bill.TotalAmount - totalReceivedValue;

            if (variance > tolerance)
            {
                bill.MatchStatus = BillMatchStatus.Variance;
                bill.MatchVarianceReason = $"Bill Total ({bill.TotalAmount:C}) exceeds Value of Goods Received ({totalReceivedValue:C}). Variance: {variance:C}";
                await ctx.SaveChangesAsync();
                return bill.MatchVarianceReason; // Return error to block posting
            }

            bill.MatchStatus = BillMatchStatus.Matched;
            bill.MatchVarianceReason = null;
            await ctx.SaveChangesAsync();
            return string.Empty; // Success
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

            // 1. Validate inputs
            if (grn.Lines.All(l => l.QuantityReceived <= 0)) return "No quantity received.";
            if (warehouseId == Guid.Empty) return "Warehouse is required.";

            // 2. Fetch Parent PO to get Costs and Vendor (Required for WACC & Journal)
            var po = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId);

            if (po == null) return "Parent Purchase Order not found.";

            // 3. Process the Receipt Header
            if (grn.Id == Guid.Empty) grn.Id = Guid.NewGuid();
            ctx.GoodsReceipts.Add(grn);

            // Save GRN first so we have an ID (optional, but good for data integrity)
            await ctx.SaveChangesAsync();

            // 4. LOOP LINES -> TRIGGER INVENTORY SERVICE
            foreach (var grnLine in grn.Lines.Where(l => l.QuantityReceived > 0))
            {
                var poLine = po.Lines.FirstOrDefault(l => l.Id == grnLine.PurchaseOrderLineId);
                if (poLine == null) continue;

                // Calculate Value of this specific receipt batch for WACC
                decimal landedCost = grnLine.QuantityReceived * poLine.UnitCost;

                // CALL THE INVENTORY ENGINE
                // This updates WACC, Stock Ledger, and Posts the Journal
                var err = await _inventoryService.ReceiveStockAsync(
                    grn.CompanyId,
                    poLine.ItemId,
                    warehouseId,
                    grnLine.QuantityReceived,
                    landedCost,
                    po.VendorId,
                    grn.GrnNumber // Reference string for Audit Trail
                );

                if (!string.IsNullOrEmpty(err))
                {
                    // If one line fails, we should probably stop and report it
                    return $"Error receiving Item {poLine.ItemId}: {err}";
                }
            }

            return string.Empty;
        }

        public async Task<List<PurchaseOrder>> GetOpenPOsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Fetches all POs for the company. 
            // In a future phase, you might filter this by p.Status != POStatus.Closed
            return await ctx.PurchaseOrders
                .AsNoTracking() // Performance optimization for read-only lists
                .Include(p => p.Lines)
                .Where(p => p.CompanyId == companyId)
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();
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


            // --- 3. EXECUTE POSTING (If we get here, data is valid) ---
            var glLines = new List<GLJournalLine>();
            var vendor = await ctx.Vendors.FindAsync(bill.VendorId);
            string vendorName = vendor?.Name ?? "Unknown";

            // Debits
            foreach (var line in bill.Lines)
            {
                glLines.Add(new GLJournalLine
                {
                    AccountId = line.ExpenseGlAccountId,
                    Debit = line.LineTotal,
                    Credit = 0,
                    Reference = $"Bill: "
                });
            }

            // Credit
            glLines.Add(new GLJournalLine
            {
                AccountId = bill.AccountsPayableGlId,
                Debit = 0,
                Credit = bill.TotalAmount,
                Reference = $"Inv #{bill.ExternalInvoiceNumber} - {vendorName}"
            });

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