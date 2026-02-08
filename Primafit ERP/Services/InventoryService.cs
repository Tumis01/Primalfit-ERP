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

        // 1. RECEIVE STOCK (Procurement + WACC Calculation)
        public async Task<string> ReceiveStockAsync(Guid companyId, Guid itemId, Guid warehouseId, decimal qty, decimal totalLandedCost, Guid vendorId, string reference)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch Item & Vendor
            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            var vendor = await ctx.Vendors.FindAsync(vendorId);
            if (vendor?.PayablesAccountId == null) return "Vendor Payables Account missing.";

            // --- A. SERVICE ITEM LOGIC ---
            if (item.IsService)
            {
                // ... (Keep your existing Service logic here) ...
                return string.Empty;
            }

            // --- B. PHYSICAL GOODS LOGIC ---

            // 1. Get Current Quantity (Summing the Ledger, just like your View does)
            decimal currentTotalQty = await ctx.StockLedgers
                .Where(s => s.ItemId == itemId)
                .SumAsync(s => s.QuantityChanged);

            // Prevent negative history from breaking WACC (optional safety)
            if (currentTotalQty < 0) currentTotalQty = 0;

            // 2. Calculate New WACC
            decimal oldWacc = item.WeightedAverageCost;
            decimal oldValuation = currentTotalQty * oldWacc;
            decimal newValuation = oldValuation + totalLandedCost;
            decimal newTotalQty = currentTotalQty + qty;

            // 3. Update Item WACC (This is the only field we change on Item)
            if (newTotalQty > 0)
            {
                item.WeightedAverageCost = newValuation / newTotalQty;

                // Log WACC History
                ctx.ItemCostHistories.Add(new ItemCostHistory
                {
                    ItemId = itemId,
                    OldQty = currentTotalQty,
                    OldWacc = oldWacc,
                    NewQtyIn = qty,
                    NewCostIn = qty > 0 ? totalLandedCost / qty : 0,
                    ResultingWacc = item.WeightedAverageCost,
                    Reference = reference,
                    DateChanged = DateTime.UtcNow
                });
            }

            
            var ledgerEntry = new StockLedger
            {
                Id = Guid.NewGuid(), // Ensure ID is generated
                CompanyId = companyId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                QuantityChanged = qty, // +Quantity
                Type = StockMovementType.Purchase,
                CostAtTime = item.WeightedAverageCost,
                Reference = reference,
                Date = DateTime.UtcNow
            };

            ctx.StockLedgers.Add(ledgerEntry);

            // 5. Financial Posting (GL)
            var glLines = new List<GLJournalLine>
    {
        new() { AccountId = item.InventoryAssetAccountId, Debit = totalLandedCost, Credit = 0, Reference = $"Stock In: {item.Name}" },
        new() { AccountId = vendor.PayablesAccountId.Value, Debit = 0, Credit = totalLandedCost, Reference = $"Bill: {vendor.Name}" }
    };

            await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Purchase", $"Stock In - {item.Name}", glLines);

            // 6. Save Everything
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        // 2. ISSUE TO PROJECT (New Phase 2 Logic)
        public async Task<string> IssueToProjectAsync(Guid companyId, Guid itemId, Guid warehouseId, Guid projectId, decimal qty, string note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            decimal currentQty = await GetStockLevel(itemId, warehouseId);
            if (currentQty < qty) return $"Insufficient stock. Available: {currentQty}";

            decimal issueValue = qty * item.WeightedAverageCost;

            ctx.StockLedgers.Add(new StockLedger
            {
                CompanyId = companyId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                QuantityChanged = -qty,
                Type = StockMovementType.Sale,
                CostAtTime = item.WeightedAverageCost,
                Reference = $"PRJ: {note}"
            });

            var glLines = new List<GLJournalLine>
            {
                new() { AccountId = item.CostOfGoodsSoldAccountId, Debit = issueValue, Credit = 0, Reference = $"Project Use: {item.Name}",  },
                new() { AccountId = item.InventoryAssetAccountId, Debit = 0, Credit = issueValue, Reference = $"Issued from {warehouseId}" }
            };

            await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Project Issue", note, glLines);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 3. SHIP TRANSFER (Restored)
        public async Task<string> ShipTransferAsync(Guid companyId, Guid itemId, Guid fromWhId, Guid toWhId, decimal qty, Guid transitAccountId, string note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            decimal available = await GetStockLevel(itemId, fromWhId);
            if (available < qty) return $"Insufficient stock. Available: {available}";

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
                ValueAtShipment = qty * item.WeightedAverageCost,
                Reference = note
            };
            ctx.StockTransfers.Add(transfer);

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

            var glLines = new List<GLJournalLine>
            {
                new() { AccountId = transitAccountId, Debit = transfer.ValueAtShipment, Credit = 0, Reference = "Transit" },
                new() { AccountId = item.InventoryAssetAccountId, Debit = 0, Credit = transfer.ValueAtShipment, Reference = "Shipment" }
            };

            await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Ship", note, glLines);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 4. RECEIVE TRANSFER (Restored)
        public async Task<string> ReceiveTransferAsync(Guid transferId, decimal actualQtyReceived)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var transfer = await ctx.StockTransfers.Include(t => t.Item).FirstOrDefaultAsync(t => t.Id == transferId);
            if (transfer == null) return "Transfer not found.";

            transfer.Status = TransferStatus.Received;
            transfer.DateReceived = DateTime.UtcNow;
            transfer.QuantityReceived = actualQtyReceived;

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

            decimal totalShippedValue = transfer.ValueAtShipment;
            decimal receivedValue = actualQtyReceived * transfer.Item.WeightedAverageCost;
            decimal lostValue = totalShippedValue - receivedValue;

            var glLines = new List<GLJournalLine>
            {
                new() { AccountId = transfer.TransitGLAccountId, Debit = 0, Credit = totalShippedValue, Reference = "Clear Transit" },
                new() { AccountId = transfer.Item.InventoryAssetAccountId, Debit = receivedValue, Credit = 0, Reference = "Receipt" }
            };

            if (lostValue > 0)
            {
                glLines.Add(new GLJournalLine
                {
                    AccountId = transfer.Item.AdjustmentExpenseAccountId,
                    Debit = lostValue,
                    Credit = 0,
                    Reference = "Transit Loss"
                });
            }

            await _glOps.CreateJournalEntryAsync(transfer.CompanyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Recv", transfer.Reference, glLines);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 5. GET PENDING TRANSFERS (Restored)
        public async Task<List<StockTransfer>> GetPendingTransfersAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.StockTransfers
                .Include(t => t.Item).Include(t => t.FromWarehouse).Include(t => t.ToWarehouse)
                .Where(t => t.CompanyId == companyId && t.Status == TransferStatus.InTransit)
                .OrderByDescending(t => t.DateShipped)
                .ToListAsync();
        }

        // 6. HELPER
        public async Task<decimal> GetStockLevel(Guid itemId, Guid warehouseId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.StockLedgers
                .Where(s => s.ItemId == itemId && s.WarehouseId == warehouseId)
                .SumAsync(s => s.QuantityChanged);
        }
    }
}