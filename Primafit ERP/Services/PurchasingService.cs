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
            // 1. Sanitize Inputs
            if (bill.MatchVarianceReason == null) bill.MatchVarianceReason = "";
            if (bill.ExternalInvoiceNumber == null) bill.ExternalInvoiceNumber = "";

            if (bill.CompanyId == Guid.Empty) return "System Error: Bill has no Company ID.";
            if (bill.VendorId == Guid.Empty) return "Vendor is required.";
            if (bill.Lines == null || bill.Lines.Count == 0) return "Bill must have at least one line.";

            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 2. Load Existing (Include Lines to handle replacement)
            var existing = await ctx.VendorBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == bill.Id);

            try
            {
                if (existing == null)
                {
                    // --- CREATE NEW ---
                    if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();

                    foreach (var line in bill.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.VendorBillId = bill.Id;
                    }

                    ctx.VendorBills.Add(bill);
                    // Note: EF Core usually handles children automatically if added to parent, 
                    // but explicit addition is safer in detached scenarios.
                    ctx.VendorBillLines.AddRange(bill.Lines);
                }
                else
                {
                    // --- UPDATE EXISTING ---

                    // Check if already posted (Safety Guard)
                    if (existing.IsPosted) return "STOP: Cannot edit a bill that has already been posted.";

                    // Preserve critical fields that shouldn't change on edit
                    bill.CompanyId = existing.CompanyId;
                    bill.IsPosted = existing.IsPosted;
                    bill.PostedDate = existing.PostedDate;

                    // Update Header
                    ctx.Entry(existing).CurrentValues.SetValues(bill);

                    // Replace Lines (Clear old, Insert new)
                    ctx.VendorBillLines.RemoveRange(existing.Lines);

                    foreach (var line in bill.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.VendorBillId = bill.Id; // Ensure Link
                    }
                    ctx.VendorBillLines.AddRange(bill.Lines);
                }

                // 3. Integrity Checks
                // Ensure Vendor exists
                if (!await ctx.Vendors.AnyAsync(v => v.Id == bill.VendorId))
                    return "STOP: Selected Vendor does not exist.";

                // Ensure PO exists (if linked)
                if (bill.PurchaseOrderId.HasValue && bill.PurchaseOrderId != Guid.Empty)
                {
                    if (!await ctx.PurchaseOrders.AnyAsync(p => p.Id == bill.PurchaseOrderId))
                        return "STOP: Linked Purchase Order does not exist.";
                }

                await ctx.SaveChangesAsync();
                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                return $"DATABASE ERROR: {ex.Message}";
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
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // A. Basic Validation
            if (po.VendorId == Guid.Empty) return "Vendor is required.";
            if (po.Lines.Count == 0) return "Order must have at least one line.";

           
            
            // 1. Get all Items involved in this PO to find their GL Accounts
            var itemIds = po.Lines.Select(l => l.ItemId).Distinct().ToList();
            var items = await ctx.Items
                .AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .ToListAsync();

            // 2. Map PO Lines to Budget Requests (GL Account + Amount)
            var budgetRequests = new List<(Guid SegCoaId, decimal Amount)>();

            foreach (var line in po.Lines)
            {
                var item = items.FirstOrDefault(i => i.Id == line.ItemId);
                if (item != null)
                {
                    Guid targetAccount = item.IsService ? item.CostOfGoodsSoldAccountId : item.InventoryAssetAccountId;
                    decimal lineTotal = line.QuantityOrdered * line.UnitCost;
                    budgetRequests.Add((targetAccount, lineTotal));
                }
            }

            // 3. Perform the Check
            if (budgetRequests.Any())
            {
                // Pass CompanyId and the list of (Account, Amount) to the Budget Service
                string budgetError = await _budgetService.ValidateFundsAsync(po.CompanyId, budgetRequests);
                
                if (!string.IsNullOrEmpty(budgetError))
                {
                    // HARD STOP: Return the error immediately. Do not save.
                    return $"BUDGET STOP: {budgetError}"; 
                }
            }
            
            

            var existing = await ctx.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == po.Id);

            try
            {
                if (existing == null)
                {
                    // --- CREATE NEW ---
                    if (po.Id == Guid.Empty) po.Id = Guid.NewGuid();
                    
                    foreach (var line in po.Lines)
                    {
                        if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                        line.PurchaseOrderId = po.Id;
                        ctx.PurchaseOrderLines.Add(line);
                    }
                    ctx.PurchaseOrders.Add(po);
                }
                else
                {
                    
                    
                    po.CompanyId = existing.CompanyId;
                    ctx.Entry(existing).CurrentValues.SetValues(po);

                    ctx.PurchaseOrderLines.RemoveRange(existing.Lines);
                    foreach (var line in po.Lines)
                    {
                        line.PurchaseOrderId = po.Id;
                        ctx.PurchaseOrderLines.Add(line);
                    }
                }

                await ctx.SaveChangesAsync();
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
                // 1. Basic Validations
                if (grn.CompanyId == Guid.Empty) return "STOP: No CompanyId.";
                if (grn.PurchaseOrderId == Guid.Empty) return "STOP: No Purchase Order selected.";
                if (warehouseId == Guid.Empty) return "STOP: No Warehouse selected.";
                if (grn.Lines == null || grn.Lines.Count == 0) return "STOP: GRN has no lines.";

                // 2. Fetch PO (Include Lines to avoid database round-trips in the loop)
                var po = await ctx.PurchaseOrders
                    .Include(p => p.Lines)
                    .FirstOrDefaultAsync(p => p.Id == grn.PurchaseOrderId && p.CompanyId == grn.CompanyId);

                if (po == null) return "STOP: PO not found.";
                if (po.IsInvoicePosted) return "STOP: Invoice already posted. Cannot receive again.";

                // 3. Prepare GRN Header
                if (grn.Id == Guid.Empty) grn.Id = Guid.NewGuid();
                if (string.IsNullOrWhiteSpace(grn.GrnNumber))
                    grn.GrnNumber = $"GRN-{DateTime.Now:yyMM}-{Random.Shared.Next(100, 999)}";

                // 4. Process Lines
                foreach (var line in grn.Lines)
                {
                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                    line.GoodsReceiptId = grn.Id;
                    if (line.QuantityReceived < 0) return "STOP: Negative qty not allowed.";
                }

                // Add GRN to Context
                ctx.GoodsReceipts.Add(grn);

                // 5. Create Stock Ledger Entries (Physical Movement)
                foreach (var grnLine in grn.Lines.Where(l => l.QuantityReceived > 0))
                {
                    // Find corresponding PO Line (Loaded in memory via Include above)
                    var poLine = po.Lines.FirstOrDefault(l => l.Id == grnLine.PurchaseOrderLineId);

                    if (poLine == null)
                        return $"STOP: PO line not found for GRN Line ID {grnLine.Id}";

                    // Create Ledger Entry
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

                        // IMPORTANT: Set provisional cost to PO Price. 
                        // The Valuation Engine will update the Item Master WACC shortly.
                        CostAtTime = poLine.UnitCost
                    });
                }

                
                po.HasReceipt = true;
                await ctx.SaveChangesAsync();
                //await _valuationService.RecalculateWACC(grn.Id);
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

            var bill = await ctx.VendorBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null) return "Bill not found.";
            if (bill.IsPosted) return "STOP: This bill has already been posted.";
            if (bill.AccountsPayableGlId == Guid.Empty) return "STOP: The AP Account is not set.";

            // 1. Validate Accounts in SegCOA
            bool apExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == bill.AccountsPayableGlId && a.CompanyId == bill.CompanyId && a.IsActive);
            if (!apExists) return "STOP: AP Account ID is invalid or Inactive.";

            foreach (var line in bill.Lines)
            {
                if (line.ExpenseGlAccountId == Guid.Empty) return "STOP: Line missing Expense Account.";
                bool expExists = await ctx.SegChartOfAccounts.AnyAsync(a => a.Id == line.ExpenseGlAccountId && a.CompanyId == bill.CompanyId && a.IsActive);
                if (!expExists) return "STOP: Selected Expense/Asset account is invalid or Inactive.";
            }

            // 2. Validate Period
            DateOnly postDate = DateOnly.FromDateTime(bill.BillDate);
            var period = await ctx.AccountingPeriods
                .FirstOrDefaultAsync(p => p.CompanyId == bill.CompanyId && p.StartDate <= postDate && p.EndDate >= postDate);

            if (period == null || period.IsClosed) return $"STOP: No Open Period for {postDate}.";

            // 3. Prepare GL Lines
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

            // 4. Post to GL
            var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                bill.CompanyId, postDate, "Vendor Bill",
                $"Inv #{bill.ExternalInvoiceNumber ?? "REF"}", glLines
            );

            if (!string.IsNullOrEmpty(err)) return $"GL ERROR: {err}";

            if (batchId.HasValue) await _glOps.PostBatchAsync(bill.CompanyId, batchId.Value);

            // 5. Update Status
            bill.IsPosted = true;
            bill.PostedDate = DateTime.Now;

            // Update PO Status if linked
            if (bill.PurchaseOrderId.HasValue)
            {
                var po = await ctx.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == bill.PurchaseOrderId.Value);
                if (po != null)
                {
                    po.IsInvoicePosted = true;
                    po.Status = PurchaseOrderStatus.Closed;
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}