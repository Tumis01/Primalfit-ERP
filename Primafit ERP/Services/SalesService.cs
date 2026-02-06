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
            if (!order.Lines.Any()) return "Order must have at least one line.";

            // --- NEW ORDER ---
            if (order.Id == Guid.Empty || !await ctx.SalesOrders.AnyAsync(o => o.Id == order.Id))
            {
                // ... (New order logic remains the same) ...
                order.Id = Guid.NewGuid();
                order.OrderNumber = $"SO-{DateTime.UtcNow:yyMM}-{new Random().Next(1000, 9999)}";

                foreach (var line in order.Lines)
                {
                    line.HeaderId = order.Id;
                    line.Id = Guid.NewGuid();
                    line.Header = null; // <--- SAFETY: Prevent circular tracking
                }

                ctx.SalesOrders.Add(order);
            }
            // --- UPDATE EXISTING ---
            else
            {
                var existing = await ctx.SalesOrders
                    .Include(o => o.Lines)
                    .FirstOrDefaultAsync(o => o.Id == order.Id);

                if (existing == null) return "Order not found.";
                if (existing.Status != OrderStatus.Draft) return "Cannot edit a confirmed order.";

                // 1. Update Header Fields
                existing.CustomerId = order.CustomerId;
                existing.CurrencyId = order.CurrencyId;
                existing.ExchangeRate = order.ExchangeRate;
                existing.Date = order.Date;
                existing.TaxId = order.TaxId;

                // 2. Clear Old Lines
                ctx.SalesOrderLines.RemoveRange(existing.Lines);

                // 3. Add New Lines (With Conflict Fix)
                foreach (var line in order.Lines)
                {
                    var newLine = new SalesOrderLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = existing.Id,      // Link to the TRACKED existing header
                        ItemId = line.ItemId,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice,
                        // Do NOT set 'Header = order' here. 
                        // We strictly use the Foreign Key (HeaderId).
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
            var order = await ctx.SalesOrders.Include(o => o.Lines).ThenInclude(l => l.Item).FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return "Order not found.";
            if (order.Status != OrderStatus.Draft && order.Status != OrderStatus.Confirmed) return "Invalid Order Status.";

            var glLines = new List<GLJournalLine>();

            foreach (var line in order.Lines)
            {
                if (line.Item == null || line.Item.IsService) continue;

                decimal currentStock = await _invService.GetStockLevel(line.ItemId, warehouseId);
                if (currentStock < line.Quantity)
                    return $"Insufficient stock for {line.Item.Name}. Have: {currentStock}, Need: {line.Quantity}";

                ctx.StockLedgers.Add(new StockLedger
                {
                    CompanyId = order.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = warehouseId,
                    QuantityChanged = -line.Quantity,
                    Type = StockMovementType.Sale,
                    CostAtTime = line.Item.WeightedAverageCost,
                    Reference = order.OrderNumber
                });

                decimal costVal = line.Quantity * line.Item.WeightedAverageCost;
                if (costVal > 0)
                {
                    glLines.Add(new GLJournalLine { AccountId = line.Item.CostOfGoodsSoldAccountId, Debit = costVal, Credit = 0, Reference = $"COGS {line.Item.SKU}" });
                    glLines.Add(new GLJournalLine { AccountId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = costVal, Reference = $"Stock Out {line.Item.SKU}" });
                }
            }

            if (glLines.Any())
            {
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Shipment", $"Ship {order.OrderNumber}", glLines);
                if (!string.IsNullOrEmpty(err)) return err;
                order.ShipmentBatchId = batchId;
            }

            order.Status = OrderStatus.Shipped;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // (Existing methods omitted for brevity, focusing on the Posting logic)

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
        public async Task<string> InvoiceOrderAsync(Guid orderId, Guid? warehouseId = null)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync(); // Wrap in transaction

            try
            {
                var order = await ctx.SalesOrders
                    .Include(o => o.Customer)
                    .Include(o => o.Lines).ThenInclude(l => l.Item)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null) return "Order not found.";

                // Validation: Allow Confirmed OR Shipped
                if (order.Status == OrderStatus.Draft) return "Order must be confirmed first.";
                if (order.Status == OrderStatus.Invoiced) return "Order is already invoiced.";

                var glLines = new List<GLJournalLine>();

                // --- STEP 1: HANDLE SHIPMENT (If not already shipped) ---
                if (order.Status == OrderStatus.Confirmed)
                {
                    // If order has physical items, we MUST have a warehouse
                    bool hasPhysicalItems = order.Lines.Any(l => l.Item != null && !l.Item.IsService);

                    if (hasPhysicalItems)
                    {
                        if (warehouseId == null || warehouseId == Guid.Empty)
                            return "Select a warehouse to fulfill physical items.";

                        foreach (var line in order.Lines)
                        {
                            if (line.Item == null || line.Item.IsService) continue;

                            // 1a. Check Stock
                            decimal currentStock = await _invService.GetStockLevel(line.ItemId, warehouseId.Value);
                            if (currentStock < line.Quantity)
                                return $"Insufficient stock for {line.Item.Name}.";

                            // 1b. Deduct Stock
                            ctx.StockLedgers.Add(new StockLedger
                            {
                                CompanyId = order.CompanyId,
                                ItemId = line.ItemId,
                                WarehouseId = warehouseId.Value,
                                QuantityChanged = -line.Quantity,
                                Type = StockMovementType.Sale,
                                CostAtTime = line.Item.WeightedAverageCost,
                                Reference = order.OrderNumber
                            });

                            // 1c. COGS GL (Debit COGS, Credit Inventory)
                            decimal costVal = line.Quantity * line.Item.WeightedAverageCost;
                            if (costVal > 0)
                            {
                                glLines.Add(new GLJournalLine { AccountId = line.Item.CostOfGoodsSoldAccountId, Debit = costVal, Credit = 0, Reference = $"COGS {line.Item.SKU}" });
                                glLines.Add(new GLJournalLine { AccountId = line.Item.InventoryAssetAccountId, Debit = 0, Credit = costVal, Reference = $"Stock Out {line.Item.SKU}" });
                            }
                        }
                    }
                    // If only services, we skip the stock logic but still proceed.
                }

                // --- STEP 2: HANDLE INVOICE (Revenue) ---

                // 1. Calculate Base Totals
                decimal subTotal = order.Lines.Sum(l => l.LineTotal);
                decimal taxAmount = 0;
                Guid? taxAccountId = null;

                if (order.TaxId.HasValue)
                {
                    var tax = await ctx.Taxes.FindAsync(order.TaxId.Value);
                    if (tax != null)
                    {
                        taxAmount = subTotal * (tax.Per / 100);
                        // Assuming Tax model has GLAccountId. If not, you need to add it or fetch a default.
                        // taxAccountId = tax.GLAccountId; 
                    }
                }

                decimal grandTotal = subTotal + taxAmount;

                // 2. Dr Accounts Receivable
                if (order.Customer?.ReceivablesAccountId == null) return "Customer AR Account missing.";

                glLines.Add(new GLJournalLine
                {
                    AccountId = order.Customer.ReceivablesAccountId.Value,
                    Debit = grandTotal,
                    Credit = 0,
                    Reference = $"Inv {order.OrderNumber}"
                });

                // 3. Cr Sales Revenue
                foreach (var line in order.Lines)
                {
                    glLines.Add(new GLJournalLine
                    {
                        AccountId = line.Item.SalesIncomeAccountId,
                        Debit = 0,
                        Credit = line.LineTotal,
                        Reference = $"Rev {line.Item.Name}"
                    });
                }

                // 4. Cr Tax Liability
                // (Add logic here if you have the Tax Account ID)

                // --- STEP 3: POST GL BATCH ---
                if (glLines.Any())
                {
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(order.CompanyId, order.Date, "Invoice", $"Inv {order.OrderNumber}", glLines);
                    if (!string.IsNullOrEmpty(err)) throw new Exception(err); // Rollback

                    order.InvoiceBatchId = batchId;
                }

                order.Status = OrderStatus.Invoiced; // Jump straight to Invoiced
                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Error: {ex.Message}";
            }
        }
    }
}