using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
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

       
        public async Task<List<SalesOrder>> GetOrdersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.Lines) // <--- ADD THIS LINE (CRITICAL)
                .Where(o => o.CompanyId == companyId)
                .OrderByDescending(o => o.Date)
                .AsNoTracking()
                .ToListAsync();
        }

        // 2. GET SINGLE ORDER
        public async Task<SalesOrder?> GetOrderByIdAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == orderId);
        }

        // 3. CREATE / UPDATE ORDER
        // 3. CREATE / UPDATE ORDER
        public async Task<string> SaveOrderAsync(SalesOrder order)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (order.CompanyId == Guid.Empty) return "System Error: Company ID missing.";
            if (order.CustomerId == Guid.Empty) return "Customer is required.";
            if (order.WarehouseId == Guid.Empty) return "Fulfillment Warehouse is required.";
            if (!order.Lines.Any()) return "Order must have at least one line.";

            // INVENTORY RESERVATION CHECK
            foreach (var line in order.Lines)
            {
                var item = await ctx.Items.FindAsync(line.ItemId);
                if (item != null && !item.IsService)
                {
                    decimal availableToPromise = await _invService.GetAvailableToPromiseAsync(
                        order.CompanyId, line.ItemId, order.WarehouseId, order.Id);

                    if (line.Quantity > availableToPromise)
                        return $"Cannot reserve {line.Quantity} of {item.Name}. Only {availableToPromise} available.";
                }
            }

            // --- NEW ORDER ---
            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                order.Id = Guid.NewGuid();
                order.OrderNumber = $"SO-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";

                // FIX: Automatically Confirm new orders so they are ready for invoicing
                order.Status = OrderStatus.Confirmed;

                foreach (var line in order.Lines)
                {
                    line.HeaderId = order.Id;
                    line.Id = Guid.NewGuid();
                    line.Header = null;
                }
                ctx.SalesOrders.Add(order);
            }
            // --- UPDATE EXISTING ---
            else
            {
                var existing = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == order.Id);
                if (existing == null) return "Order not found.";

                // FIX: Allow editing anytime UNTIL it is invoiced
                if (existing.Status == OrderStatus.Invoiced) return "Cannot edit an order that has already been invoiced.";

                existing.CustomerId = order.CustomerId;
                existing.WarehouseId = order.WarehouseId;
                existing.CurrencyId = order.CurrencyId;
                existing.ExchangeRate = order.ExchangeRate;
                existing.Date = order.Date;
                existing.TaxId = order.TaxId;

                ctx.SalesOrderLines.RemoveRange(existing.Lines);

                foreach (var line in order.Lines)
                {
                    var newLine = new SalesOrderLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = existing.Id,
                        ItemId = line.ItemId,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice
                    };
                    ctx.SalesOrderLines.Add(newLine);
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 4. SHIP ORDER (Unchanged)
        public async Task<string> ShipOrderAsync(Guid orderId, Guid warehouseId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status != OrderStatus.Confirmed) return "Order must be confirmed before shipping.";

            var glLines = new List<GLJournalLine>();

            foreach (var line in order.Lines)
            {
                if (line.Item == null || line.Item.IsService) continue;

                // 1. Check Physical Stock (Since we reserved it, this should generally pass, but it's a final safety net)
                decimal currentStock = await _invService.GetStockLevel(line.ItemId, warehouseId);
                if (currentStock < line.Quantity)
                    return $"Fulfillment failed: Insufficient physical stock for {line.Item.Name}. Have: {currentStock}, Need: {line.Quantity}";

                // 2. Deduct Stock Ledger
                ctx.StockLedgers.Add(new StockLedger
                {
                    Id = Guid.NewGuid(),
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = warehouseId,
                    QuantityChanged = -line.Quantity, // Deduct
                    Type = StockMovementType.Sale,
                    CostAtTime = line.Item.WeightedAverageCost, // WACC is ALWAYS in Base Currency
                    Reference = order.OrderNumber,
                    Date = DateTime.UtcNow
                });

                // 3. Prepare COGS Journal (In Base Currency)
                decimal cogsValueBase = line.Quantity * line.Item.WeightedAverageCost;
                if (cogsValueBase > 0)
                {
                    glLines.Add(new GLJournalLine { AccountId = line.Item.CostOfGoodsSoldAccountId, Debit = cogsValueBase, Credit = 0, Reference = $"COGS {line.Item.SKU}" });
                    glLines.Add(new GLJournalLine { AccountId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = cogsValueBase, Reference = $"Stock Out {line.Item.SKU}" });
                }
            }

            // 4. Post Shipment Journal
            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Shipment", $"Ship {order.OrderNumber}", glLines);
                if (!string.IsNullOrEmpty(err)) return err;

                // Auto-post the COGS batch
                if (batchId.HasValue) await _glOps.PostBatchAsync(order.CompanyId, batchId.Value);

                order.ShipmentBatchId = batchId;
            }

            order.Status = OrderStatus.Shipped;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> PostSalesInvoiceAsync(Guid invoiceId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Fetch Invoice with lines
            var inv = await ctx.SalesInvoices
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (inv == null) return "Invoice not found";

            var glLines = new List<GLJournalLine>();
            decimal totalInvoice = inv.Lines.Sum(x => x.Amount);

            // 1. DEBIT RECEIVABLE (Asset) - User Selected Account
            glLines.Add(new GLJournalLine
            {
                AccountId = inv.ReceivablesGlAccountId,
                Debit = totalInvoice,
                Credit = 0,
                Reference = "Invoice Revenue"
            });

            // 2. CREDIT REVENUE (Income) - User Selected Account per line
            foreach (var line in inv.Lines)
            {
                glLines.Add(new GLJournalLine
                {
                    AccountId = line.RevenueGlAccountId,
                    Debit = 0,
                    Credit = line.Amount,
                    Reference = "Sales Revenue"
                });
            }

            // 3. SEND TO GL ENGINE
            var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                inv.CompanyId,
                DateOnly.FromDateTime(DateTime.Today), // Or Invoice Date
                "Sales Invoice",
                $"Inv {inv.Id.ToString().Substring(0, 8)}",
                glLines
            );

            return err;
        }
        // Add this inside SalesService.cs
        public async Task<string> TerminateOrderAsync(Guid orderId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var order = await ctx.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status == OrderStatus.Invoiced) return "Cannot terminate an order that has already been invoiced.";

            // Remove the lines and the order completely
            ctx.SalesOrderLines.RemoveRange(order.Lines);
            ctx.SalesOrders.Remove(order);

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<string> InvoiceOrderAsync(Guid orderId, Guid? warehouseId = null)
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
        
        // FIX: Removed the "Draft" block. 
        // Only block it if it has ALREADY been invoiced.
        if (order.Status == OrderStatus.Invoiced) return "Order is already invoiced.";

        var glLines = new List<GLJournalLine>();

        // --- STEP 1: AUTO-SHIP ---
        // Changed to run Auto-Ship for ANY status prior to Invoiced (Draft or Confirmed)
        bool hasPhysicalItems = order.Lines.Any(l => l.Item != null && !l.Item.IsService);
        if (hasPhysicalItems)
        {
            if (warehouseId == null || warehouseId == Guid.Empty)
                return "Select a warehouse to fulfill physical items.";

            string shipErr = await ShipOrderAsync(order.Id, warehouseId ?? Guid.Empty);
            if (!string.IsNullOrEmpty(shipErr)) return shipErr;
        }

                // --- STEP 2: CALCULATE FINANCIALS (Foreign -> Base Conversion) ---

                decimal rate = order.ExchangeRate > 0 ? order.ExchangeRate : 1;
                decimal totalCreditsBase = 0; // We will use this to GUARANTEE Debits = Credits

                // 1. Credit Sales Revenue (Income increases)
                foreach (var line in order.Lines)
                {
                    if (line.Item == null || line.Quantity == 0) continue;

                    decimal lineRevForeign = line.Quantity * line.UnitPrice;
                    decimal lineRevBase = Math.Round(lineRevForeign * rate, 2);

                    if (line.Item.SalesIncomeAccountId == Guid.Empty)
                        return $"Item '{line.Item.Name}' is missing a Sales Income GL Account mapping.";

                    glLines.Add(new GLJournalLine
                    {
                        AccountId = line.Item.SalesIncomeAccountId,
                        Debit = 0,
                        Credit = lineRevBase,
                        Reference = $"Rev {line.Item.Name}"
                    });

                    totalCreditsBase += lineRevBase; // Add to our exact credit sum
                }

                // 2. Credit Tax Liability (If Applicable)
                if (order.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(order.TaxId.Value);
                    if (tax != null && tax.Per > 0)
                    {
                        decimal subTotalForeign = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
                        decimal taxAmountForeign = subTotalForeign * (tax.Per / 100);
                        decimal taxAmountBase = Math.Round(taxAmountForeign * rate, 2);

                        // IMPORTANT FIX: If you apply a tax, you MUST post it to a Tax GL Account.
                        // Assuming your Tax.cs model has a property like 'GLAccountId':
                        /*
                        if (tax.GLAccountId == Guid.Empty) return "Tax GL Account is missing.";
                        glLines.Add(new GLJournalLine 
                        { 
                            AccountId = tax.GLAccountId, 
                            Debit = 0, 
                            Credit = taxAmountBase,
                            Reference = $"Tax {tax.TaxName}"
                        });
                        totalCreditsBase += taxAmountBase;
                        */

                        // TEMPORARY BLOCK: Until you map a GL account in your Tax model, 
                        // we must block invoices with tax so the ledger doesn't crash.
                        return "You applied Tax to this order, but the Tax GL Account is not mapped in the backend. Please test without tax for now, or map the account in SalesService.cs.";
                    }
                }

                // 3. Debit Accounts Receivable (Asset increases)
                if (order.Customer?.ReceivablesAccountId == null)
                    return "Customer AR Account is missing. Please configure it in Master Data.";

                glLines.Add(new GLJournalLine
                {
                    AccountId = order.Customer.ReceivablesAccountId.Value,
                    // We use totalCreditsBase instead of recalculating the grand total. 
                    // This perfectly eliminates 1-cent rounding errors!
                    Debit = totalCreditsBase,
                    Credit = 0,
                    Reference = $"Inv {order.OrderNumber}"
                });

                // --- STEP 4: POST GL BATCH ---
                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                        order.CompanyId,
                        order.Date,
                        "Sales Invoice",
                        $"Inv {order.OrderNumber}",
                        glLines);

                    if (!string.IsNullOrEmpty(err)) throw new Exception(err); // Triggers Rollback

                    // Auto-post the Revenue batch
                    if (batchId.HasValue) await _glOps.PostBatchAsync(order.CompanyId, batchId.Value);

                    order.InvoiceBatchId = batchId;
                }

                order.Status = OrderStatus.Invoiced;
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
    }
}