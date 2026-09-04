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
        public async Task<string> ReceiveStockAsync(Guid companyId, Guid itemId, Guid warehouseId, decimal qty, decimal totalLandedCost, Guid vendorId, string reference, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch Item & Vendor
            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            var vendor = await ctx.Vendors.FindAsync(vendorId);
            if (vendor?.PayablesAccountId == null) return "Vendor Payables Account missing.";

            var glLines = new List<GLJournalLine>();

            // --- A. SERVICE ITEM LOGIC (Financial Only, No Physical Stock) ---
            if (item.IsService)
            {
                // We do NOT write to StockLedger for services as they are not "stocked" in a warehouse.
                // We only record the expense financially.

                // Debit: Expense / COGS (Immediate expense)
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = item.CostOfGoodsSoldAccountId, // Or ExpenseAccountId if you have one
                    Debit = totalLandedCost,
                    Credit = 0,
                    Reference = $"Service Exp: {item.Name}"
                });

                // Credit: Accounts Payable
                glLines.Add(new GLJournalLine
                {
                    SegCoaId = vendor.PayablesAccountId.Value,
                    Debit = 0,
                    Credit = totalLandedCost,
                    Reference = $"Bill: {vendor.Name}"
                });

                // Stage GL for review. The reviewer is the only actor allowed to
                // create the final GL transaction rows.
                var (serviceGlError, _) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Purchase (Service)", reference, glLines, userId);
                if (!string.IsNullOrWhiteSpace(serviceGlError)) return $"GL staging failed: {serviceGlError}";

                return string.Empty; // Done for Service
            }

            // --- B. PHYSICAL GOODS LOGIC (Stock Ledger + WACC) ---

            // 1. Get Current Quantity
            decimal currentTotalQty = await ctx.StockLedgers
                .Where(s => s.ItemId == itemId)
                .SumAsync(s => s.QuantityChanged);

            if (currentTotalQty < 0) currentTotalQty = 0;

            // 2. Calculate New WACC
            decimal oldWacc = item.WeightedAverageCost;
            decimal oldValuation = currentTotalQty * oldWacc;
            decimal newValuation = oldValuation + totalLandedCost;
            decimal newTotalQty = currentTotalQty + qty;

            // 3. Update Item WACC
            if (newTotalQty > 0)
            {
                item.WeightedAverageCost = newValuation / newTotalQty;

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

            // 4. Update Stock Ledger (Physical)
            var ledgerEntry = new StockLedger
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                ItemId = itemId,
                WarehouseId = warehouseId, // Required for Physical
                QuantityChanged = qty,
                Type = StockMovementType.Purchase,
                CostAtTime = item.WeightedAverageCost,
                Reference = reference,
                Date = DateTime.UtcNow
            };

            ctx.StockLedgers.Add(ledgerEntry);

            // 5. Financial Posting (GL) - Asset vs Liability
            glLines.Add(new GLJournalLine
            {
                SegCoaId = item.InventoryAssetAccountId,
                Debit = totalLandedCost,
                Credit = 0,
                Reference = $"Stock In: {item.Name}"
            });

            glLines.Add(new GLJournalLine
            {
                SegCoaId = vendor.PayablesAccountId.Value,
                Debit = 0,
                Credit = totalLandedCost,
                Reference = $"Bill: {vendor.Name}"
            });

            var (stockGlError, stockBatchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Purchase", $"Stock In - {item.Name}", glLines, userId);
            if (!string.IsNullOrWhiteSpace(stockGlError)) return $"GL staging failed: {stockGlError}";
            ledgerEntry.GLBatchId = stockBatchId;

            // 6. Save Everything
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 2. ISSUE TO PROJECT (New Phase 2 Logic)
        public async Task<string> IssueToProjectAsync(Guid companyId, Guid itemId, Guid warehouseId, Guid projectId, decimal qty, string note, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var item = await ctx.Items.FindAsync(itemId);
            if (item == null) return "Item not found.";

            decimal currentQty = await GetStockLevel(itemId, warehouseId);
            if (currentQty < qty) return $"Insufficient stock. Available: {currentQty}";

            decimal issueValue = qty * item.WeightedAverageCost;

            var issueLedger = new StockLedger
            {
                CompanyId = companyId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                QuantityChanged = -qty,
                Type = StockMovementType.Sale,
                CostAtTime = item.WeightedAverageCost,
                Reference = $"PRJ: {note}"
            };
            ctx.StockLedgers.Add(issueLedger);

            var glLines = new List<GLJournalLine>
            {
                new() { SegCoaId  = item.CostOfGoodsSoldAccountId, Debit = issueValue, Credit = 0, Reference = $"Project Use: {item.Name}",  },
                new() { SegCoaId  = item.InventoryAssetAccountId, Debit = 0, Credit = issueValue, Reference = $"Issued from {warehouseId}" }
            };

            var (issueGlError, issueBatchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Project Issue", note, glLines, userId);
            if (!string.IsNullOrWhiteSpace(issueGlError)) return $"GL staging failed: {issueGlError}";
            issueLedger.GLBatchId = issueBatchId;
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // 3. SHIP TRANSFER (Auto-Posting to GL)
        public async Task<string> ShipTransferAsync(Guid companyId, Guid itemId, Guid fromWhId, Guid toWhId, decimal qty, Guid transitAccountId, string note, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync(); // Added Transaction Safety

            try
            {
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
                    new() { SegCoaId  = transitAccountId, Debit = transfer.ValueAtShipment, Credit = 0, Reference = "Transit" },
                    new() { SegCoaId  = item.InventoryAssetAccountId, Debit = 0, Credit = transfer.ValueAtShipment, Reference = "Shipment" }
                };

                // Create GL Batch
                var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Ship", note, glLines, userId);
                if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);

                // Auto-Post GL Batch
                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");
                    transfer.GLBatchId = batchId;
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Transfer Error: {ex.Message}";
            }
        }

        // 4. RECEIVE TRANSFER (Auto-Posting to GL)
        public async Task<string> ReceiveTransferAsync(Guid transferId, decimal actualQtyReceived, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync(); // Added Transaction Safety

            try
            {
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
                    new() { SegCoaId  = transfer.TransitGLAccountId, Debit = 0, Credit = totalShippedValue, Reference = "Clear Transit" },
                    new() { SegCoaId  = transfer.Item.InventoryAssetAccountId, Debit = receivedValue, Credit = 0, Reference = "Receipt" }
                };

                if (lostValue > 0)
                {
                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = transfer.Item.AdjustmentExpenseAccountId,
                        Debit = lostValue,
                        Credit = 0,
                        Reference = "Transit Loss"
                    });
                }

                // Create GL Batch
                var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(transfer.CompanyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Recv", transfer.Reference, glLines, userId);
                if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);

                // Auto-Post GL Batch
                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(transfer.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");
                    transfer.GLBatchId = batchId;
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Receive Error: {ex.Message}";
            }
        }

        public async Task<string> AdjustStockAsync(Guid companyId, Guid itemId, Guid warehouseId, StockEntryType adjType, decimal qty, decimal totalValueChange, string reference, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var item = await ctx.Items.FindAsync(itemId);
                if (item == null) return "Item not found.";
                if (item.IsService) return "Cannot adjust stock for service items. They are strictly expensed on receipt.";

                decimal currentTotalQty = await ctx.StockLedgers.Where(s => s.ItemId == itemId).SumAsync(s => s.QuantityChanged);
                if (currentTotalQty < 0) currentTotalQty = 0;

                decimal oldWacc = item.WeightedAverageCost;
                decimal oldValuation = currentTotalQty * oldWacc;

                decimal qtyChange = 0;
                decimal valChange = 0;

                // Determine exact changes based on Adjustment Type
                switch (adjType)
                {
                    case StockEntryType.QuantityIncrease:
                        qtyChange = qty;
                        valChange = 0; // FIX: No financial value change
                        break;
                    case StockEntryType.QuantityDecrease:
                        qtyChange = -Math.Abs(qty);
                        valChange = 0; // FIX: No financial value change
                        if (currentTotalQty + qtyChange < 0) return $"Cannot decrease quantity below zero. Current stock is {currentTotalQty}.";
                        break;
                    case StockEntryType.CostIncrease:
                        qtyChange = 0;
                        valChange = totalValueChange;
                        break;
                    case StockEntryType.CostDecrease:
                        qtyChange = 0;
                        valChange = -Math.Abs(totalValueChange);
                        if (oldValuation + valChange < 0) return "Cannot decrease cost valuation below zero.";
                        break;
                    case StockEntryType.BothIncrease:
                        qtyChange = qty;
                        valChange = totalValueChange;
                        break;
                    case StockEntryType.BothDecrease:
                        qtyChange = -Math.Abs(qty);
                        valChange = -Math.Abs(totalValueChange);
                        if (currentTotalQty + qtyChange < 0) return $"Cannot decrease quantity below zero. Current stock is {currentTotalQty}.";
                        break;
                }

                // Ensure Adjustment Account is mapped IF there is a financial value change
                if (valChange != 0 && item.AdjustmentExpenseAccountId == Guid.Empty)
                {
                    return $"STOP: Item '{item.Name}' does not have an Adjustment Expense GL Account mapped in Master Data.";
                }

                decimal newValuation = oldValuation + valChange;
                decimal newTotalQty = currentTotalQty + qtyChange;

                // Safely Recalculate WACC (Value is spread over the new quantity)
                item.WeightedAverageCost = newTotalQty > 0 ? (newValuation / newTotalQty) : 0;

                // 1. Write to History
                ctx.ItemCostHistories.Add(new ItemCostHistory
                {
                    Id = Guid.NewGuid(),
                    ItemId = itemId,
                    OldQty = currentTotalQty,
                    OldWacc = oldWacc,
                    NewQtyIn = qtyChange,
                    NewCostIn = qtyChange != 0 ? (valChange / qtyChange) : valChange,
                    ResultingWacc = item.WeightedAverageCost,
                    Reference = reference,
                    DateChanged = DateTime.UtcNow
                });

                // 2. Update Physical Stock Ledger
                if (qtyChange != 0)
                {
                    ctx.StockLedgers.Add(new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        ItemId = itemId,
                        WarehouseId = warehouseId,
                        QuantityChanged = qtyChange,
                        Type = StockMovementType.Adjustment,
                        CostAtTime = item.WeightedAverageCost,
                        Reference = reference,
                        Date = DateTime.UtcNow
                    });
                }

                // 3. Post to General Ledger ONLY if value actually changed
                if (valChange != 0)
                {
                    var glLines = new List<GLJournalLine>();

                    if (valChange > 0)
                    {
                        // Value Increased: Debit Asset, Credit Adjustment (Gain)
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = valChange, Credit = 0, Reference = $"Adj In: {reference}" });
                        glLines.Add(new GLJournalLine { SegCoaId = item.AdjustmentExpenseAccountId, Debit = 0, Credit = valChange, Reference = $"Adj Gain: {reference}" });
                    }
                    else
                    {
                        // Value Decreased: Debit Adjustment (Loss/Expense), Credit Asset
                        decimal absVal = Math.Abs(valChange);
                        glLines.Add(new GLJournalLine { SegCoaId = item.AdjustmentExpenseAccountId, Debit = absVal, Credit = 0, Reference = $"Adj Loss: {reference}" });
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = 0, Credit = absVal, Reference = $"Adj Out: {reference}" });
                    }

                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Inventory Adjustment", reference, glLines, userId);
                    if (!string.IsNullOrEmpty(err)) throw new Exception($"GL Error: {err}");
                    if (batchId.HasValue) await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Error adjusting stock: {ex.Message}";
            }
        }

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


        public async Task<decimal> GetAvailableToPromiseAsync(Guid companyId, Guid itemId, Guid warehouseId, Guid? excludeOrderId = null)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            decimal physicalStock = await ctx.StockLedgers
                .Where(s => s.ItemId == itemId && s.WarehouseId == warehouseId)
                .SumAsync(s => s.QuantityChanged);

            var reservedQuery = ctx.SalesOrderLines
                .Include(l => l.Header)
                .Where(l => l.ItemId == itemId
                         && l.Header.WarehouseId == warehouseId
                         && l.Header.CompanyId == companyId
                         && (l.Header.Status == OrderStatus.Draft || l.Header.Status == OrderStatus.Confirmed));

            if (excludeOrderId.HasValue && excludeOrderId.Value != Guid.Empty)
            {
                reservedQuery = reservedQuery.Where(l => l.HeaderId != excludeOrderId.Value);
            }

            decimal reservedStock = await reservedQuery.SumAsync(l => l.Quantity);

            return physicalStock - reservedStock;
        }
        public async Task<string> PostInventoryAdjustmentBatchAsync(
            Guid companyId,
            DateOnly postingDate,
            Guid balancingGlAccountId,
            List<InventoryAdjustmentLineDto> adjustmentLines,
            string userId)
        {
            if (balancingGlAccountId == Guid.Empty) return "STOP: A valid Balancing/Offset GL Account is required.";

            var validLines = adjustmentLines.Where(x => x.ItemId != Guid.Empty && (x.QuantityChange != 0 || x.TotalValueChange != 0)).ToList();
            if (!validLines.Any()) return "STOP: No active lines with structural adjustments were provided.";

            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var glLines = new List<GLJournalLine>();

                foreach (var line in validLines)
                {
                    var item = await ctx.Items.FindAsync(line.ItemId);
                    if (item == null) return $"Item with ID '{line.ItemId}' could not be resolved.";
                    if (item.IsService) return $"STOP: '{item.Name}' is a service item. Physical inventory parameters cannot be adjusted.";
                    if (item.InventoryAssetAccountId == Guid.Empty) return $"CONFIGURATION ERROR: '{item.Name}' is missing an Inventory Asset GL Account mapping.";

                    // 1. Resolve Current Quantities Across All Warehouses for WACC Tracking
                    decimal currentTotalQty = await ctx.StockLedgers
                        .Where(s => s.ItemId == line.ItemId)
                        .SumAsync(s => s.QuantityChanged);
                    if (currentTotalQty < 0) currentTotalQty = 0;

                    decimal oldWacc = item.WeightedAverageCost;
                    decimal oldValuation = currentTotalQty * oldWacc;

                    // 2. Handle Directional Financial Ledger Adjustments
                    decimal finalLineCostChange = Math.Abs(line.TotalValueChange);

                    if (line.IsDebitInventory)
                    {
                        // Debit Inventory Asset (Asset Up), Credit Balancing Account
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = finalLineCostChange, Credit = 0, Reference = $"Inv Adj Dr - {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = balancingGlAccountId, Debit = 0, Credit = finalLineCostChange, Reference = $"Inv Adj Bal Cr - {item.Name}" });
                    }
                    else
                    {
                        // Credit Inventory Asset (Asset Down), Debit Balancing Account
                        glLines.Add(new GLJournalLine { SegCoaId = balancingGlAccountId, Debit = finalLineCostChange, Credit = 0, Reference = $"Inv Adj Bal Dr - {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = 0, Credit = finalLineCostChange, Reference = $"Inv Adj Cr - {item.Name}" });
                    }

                    // 3. Compute Stock Valuation Shifts
                    decimal absoluteValueShift = line.IsDebitInventory ? finalLineCostChange : -finalLineCostChange;
                    decimal newTotalQty = currentTotalQty + line.QuantityChange;
                    decimal newValuation = oldValuation + absoluteValueShift;

                    if (newTotalQty < 0) return $"VALUATION BLOCKED: Adjustment forces absolute quantity of '{item.Name}' below zero to ({newTotalQty}). Transaction aborted.";

                    // Update WACC values natively if stock balances remain positive
                    item.WeightedAverageCost = newTotalQty > 0 ? Math.Round(newValuation / newTotalQty, 4) : 0;

                    // 4. Append Cost Change Audit Records
                    ctx.ItemCostHistories.Add(new ItemCostHistory
                    {
                        Id = Guid.NewGuid(),
                        ItemId = item.Id,
                        OldQty = currentTotalQty,
                        OldWacc = oldWacc,
                        NewQtyIn = line.QuantityChange,
                        NewCostIn = line.QuantityChange != 0 ? absoluteValueShift / line.QuantityChange : absoluteValueShift,
                        ResultingWacc = item.WeightedAverageCost,
                        Reference = $"Batch Adjustment - {postingDate:yyyy-MM-dd}",
                        DateChanged = DateTime.UtcNow
                    });

                    // 5. Append Physical Warehouse Movement Logging entries
                    if (line.QuantityChange != 0)
                    {
                        ctx.StockLedgers.Add(new StockLedger
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = companyId,
                            ItemId = item.Id,
                            WarehouseId = line.WarehouseId,
                            QuantityChanged = line.QuantityChange,
                            Type = StockMovementType.Adjustment,
                            CostAtTime = item.WeightedAverageCost,
                            Reference = $"ADJ-{postingDate:yyMMdd}",
                            Date = DateTime.UtcNow
                        });
                    }
                }

                // 6. Pack entries into general ledger batches
                string batchRefName = $"INVADJ-{DateTime.UtcNow:yyMMddHHmm}";
                var (glError, batchId) = await _glOps.CreateJournalEntryAsync(companyId, postingDate, batchRefName, "Inventory Batch Sub-ledger Adjustment", glLines, userId);
                if (!string.IsNullOrEmpty(glError)) throw new Exception(glError);

                if (batchId.HasValue)
                {
                    var postError = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postError)) throw new Exception(postError);
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"INVENTORY SYSTEM ADJ ERROR: {ex.Message}";
            }
        }
    }
}
