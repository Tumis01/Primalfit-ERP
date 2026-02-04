using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class InventoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;

        public InventoryService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
        }

        // 1. RECEIVE STOCK (Procurement)
        public async Task<string> ReceiveStockAsync(Guid companyId, Guid itemId, Guid warehouseId, decimal qty, decimal totalLandedCost, Guid vendorId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            var vendor = await ctx.BusinessPartners.FindAsync(vendorId);
            if (vendor?.PayablesAccountId == null) return "Vendor Payables Account missing.";

            // === LOGIC CHANGE: SERVICE ITEMS ===
            if (item.IsService)
            {
                // Services are expensed immediately. No Stock Ledger entry.
                // Dr Expense (COGS Account usually holds service cost) | Cr Vendor (AP)

                var serviceGlLines = new List<GLJournalLine>
                {
                    new() { AccountId = item.CostOfGoodsSoldAccountId, Debit = totalLandedCost, Credit = 0, Reference = $"Service Exp: {item.Name}" },
                    new() { AccountId = vendor.PayablesAccountId.Value, Debit = 0, Credit = totalLandedCost, Reference = $"Bill: {vendor.Name}" }
                };

                await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Service Bill", $"Bill for {item.Name}", serviceGlLines);
                await ctx.SaveChangesAsync();
                return string.Empty;
            }

            // === PHYSICAL GOODS LOGIC ===

            // A. WACC Calculation
            // Get current stock across ALL warehouses for valuation consistency
            decimal currentTotalQty = await ctx.StockLedgers.Where(s => s.ItemId == itemId).SumAsync(s => s.QuantityChanged);
            if (currentTotalQty < 0) currentTotalQty = 0;

            decimal oldValuation = currentTotalQty * item.WeightedAverageCost;
            decimal newValuation = oldValuation + totalLandedCost;
            decimal newTotalQty = currentTotalQty + qty;

            if (newTotalQty > 0)
                item.WeightedAverageCost = newValuation / newTotalQty; // Update Cost

            // B. Update Physical Ledger
            var ledgerEntry = new StockLedger
            {
                CompanyId = companyId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                QuantityChanged = qty,
                Type = StockMovementType.Purchase,
                CostAtTime = item.WeightedAverageCost,
                Reference = $"PURCH-{DateTime.Now:MMdd}"
            };
            ctx.StockLedgers.Add(ledgerEntry);

            // C. Post Financials (GL) -> Dr Inventory Asset, Cr Accounts Payable
            var glLines = new List<GLJournalLine>
            {
                new() { AccountId = item.InventoryAssetAccountId, Debit = totalLandedCost, Credit = 0, Reference = $"Stock In: {item.Name}" },
                new() { AccountId = vendor.PayablesAccountId.Value, Debit = 0, Credit = totalLandedCost, Reference = $"Bill: {vendor.Name}" }
            };

            await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Purchase", $"Stock In - {item.Name}", glLines);

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 2. SHIP TRANSFER (Source -> Transit)
        public async Task<string> ShipTransferAsync(Guid companyId, Guid itemId, Guid fromWhId, Guid toWhId, decimal qty, Guid transitAccountId, string note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            // VALIDATION: Services cannot be transferred
            if (item.IsService) return "Error: Services cannot be transferred between warehouses.";

            // Validate Stock
            decimal available = await GetStockLevel(itemId, fromWhId); // Helper method usage
            if (available < qty) return $"Insufficient stock at source. Available: {available}";

            var fromWh = await ctx.Warehouses.FindAsync(fromWhId);
            var toWh = await ctx.Warehouses.FindAsync(toWhId);
            if (fromWh == null || toWh == null) return "Invalid warehouses.";

            // A. Create Transfer Record
            var transfer = new StockTransfer
            {
                CompanyId = companyId,
                ItemId = itemId,
                FromWarehouseId = fromWhId,
                ToWarehouseId = toWhId,
                Quantity = qty,
                Status = TransferStatus.InTransit,
                DateShipped = DateTime.UtcNow,
                TransitGLAccountId = transitAccountId,
                ValueAtShipment = qty * item.WeightedAverageCost, // Snapshot cost
                Reference = note
            };
            ctx.StockTransfers.Add(transfer);

            // B. Reduce Physical Stock at Source
            ctx.StockLedgers.Add(new StockLedger
            {
                CompanyId = companyId,
                ItemId = itemId,
                WarehouseId = fromWhId,
                QuantityChanged = -qty,
                Type = StockMovementType.TransferOut,
                CostAtTime = item.WeightedAverageCost,
                Reference = $"SHIP: {note}"
            });

            // C. GL: Credit Inventory (Source) -> Debit Transit (Suspense)
            var glLines = new List<GLJournalLine>
            {
                new() { AccountId = transitAccountId, Debit = transfer.ValueAtShipment, Credit = 0, Reference = $"Transit to {toWh.Name}" },
                new() { AccountId = item.InventoryAssetAccountId, Debit = 0, Credit = transfer.ValueAtShipment, Reference = $"Ship from {fromWh.Name}" }
            };
            await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Ship", note, glLines);

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 3. RECEIVE TRANSFER (Transit -> Destination)
        public async Task<string> ReceiveTransferAsync(Guid transferId, decimal actualQtyReceived)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var transfer = await ctx.StockTransfers.Include(t => t.Item).FirstOrDefaultAsync(t => t.Id == transferId);
            if (transfer == null) return "Transfer not found.";
            if (transfer.Status != TransferStatus.InTransit) return "Transfer already processed.";

            if (actualQtyReceived > transfer.Quantity) return "Error: Cannot receive more than was shipped.";

            // A. Update Status
            transfer.Status = TransferStatus.Received;
            transfer.DateReceived = DateTime.UtcNow;
            transfer.QuantityReceived = actualQtyReceived;

            // B. Physical Stock Update (Only add what actually arrived)
            ctx.StockLedgers.Add(new StockLedger
            {
                CompanyId = transfer.CompanyId,
                ItemId = transfer.ItemId,
                WarehouseId = transfer.ToWarehouseId,
                QuantityChanged = actualQtyReceived,
                Type = StockMovementType.TransferIn,
                CostAtTime = transfer.Item.WeightedAverageCost,
                Reference = $"RECV: {transfer.Reference}"
            });

            // C. Financial Clearing (Handle Variance/Loss)
            decimal totalShippedValue = transfer.ValueAtShipment;
            // Value of goods that actually arrived
            decimal receivedValue = actualQtyReceived * transfer.Item.WeightedAverageCost;
            // Difference is loss
            decimal lostValue = totalShippedValue - receivedValue;

            var glLines = new List<GLJournalLine>
            {
                // Credit Transit (Clear the full suspense amount)
                new() { AccountId = transfer.TransitGLAccountId, Debit = 0, Credit = totalShippedValue, Reference = "Clear Transit" },
                
                // Debit Inventory (Asset Increase for received goods)
                new() { AccountId = transfer.Item.InventoryAssetAccountId, Debit = receivedValue, Credit = 0, Reference = "Stock Receipt" }
            };

            // If items were lost, debit the Adjustment/Loss account
            if (lostValue > 0)
            {
                glLines.Add(new GLJournalLine
                {
                    AccountId = transfer.Item.AdjustmentExpenseAccountId,
                    Debit = lostValue,
                    Credit = 0,
                    Reference = $"Loss in Transit ({transfer.Quantity - actualQtyReceived} missing)"
                });
            }

            await _glOps.CreateJournalEntryAsync(transfer.CompanyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Recv", transfer.Reference, glLines);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 4. ADJUST STOCK (Theft/Damage)
        public async Task<string> AdjustStockAsync(Guid companyId, Guid itemId, Guid warehouseId, decimal qtyToRemove, string reason)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            // Services cannot be adjusted via Stock Count
            if (item.IsService) return "Cannot adjust stock for Service items.";

            decimal costValue = qtyToRemove * item.WeightedAverageCost;

            ctx.StockLedgers.Add(new StockLedger
            {
                CompanyId = companyId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                QuantityChanged = -qtyToRemove,
                Type = StockMovementType.Adjustment,
                CostAtTime = item.WeightedAverageCost,
                Reference = $"ADJ: {reason}"
            });

            var glLines = new List<GLJournalLine>
            {
                new() { AccountId = item.AdjustmentExpenseAccountId, Debit = costValue, Credit = 0, Reference = reason },
                new() { AccountId = item.InventoryAssetAccountId, Debit = 0, Credit = costValue, Reference = $"Adjustment: {item.Name}" }
            };

            await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Inv Adj", reason, glLines);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 5. GET STOCK LEVEL (Helper)
        public async Task<decimal> GetStockLevel(Guid itemId, Guid warehouseId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.StockLedgers
                .Where(s => s.ItemId == itemId && s.WarehouseId == warehouseId)
                .SumAsync(s => s.QuantityChanged);
        }

        // 6. GET PENDING TRANSFERS
        public async Task<List<StockTransfer>> GetPendingTransfersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.StockTransfers
                .Include(t => t.Item).Include(t => t.FromWarehouse).Include(t => t.ToWarehouse)
                .Where(t => t.CompanyId == companyId && t.Status == TransferStatus.InTransit)
                .OrderByDescending(t => t.DateShipped)
                .ToListAsync();
        }
    }
}