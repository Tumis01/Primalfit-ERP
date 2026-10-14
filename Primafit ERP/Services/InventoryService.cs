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
        public async Task<string> ReceiveStockAsync(Guid companyId, Guid itemId, Guid warehouseId, decimal qty, decimal totalLandedCost, Guid vendorId, string reference, string userId, Guid? uomId = null, string? uomName = null, decimal uomConversionFactor = 1m, decimal quantityInUom = 0m)
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
                UomId = uomId,
                UomName = uomName ?? string.Empty,
                UomConversionFactor = UomConversion.NormalizeFactor(uomConversionFactor),
                QuantityInUom = quantityInUom == 0m ? UomConversion.FromBase(qty, uomConversionFactor) : quantityInUom,
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
            if (!stockBatchId.HasValue) return "GL staging failed: no review batch was created for the stock receipt.";
            var postError = await _glOps.PostBatchAsync(companyId, stockBatchId.Value, userId);
            if (!string.IsNullOrWhiteSpace(postError)) return $"GL posting failed: {postError}";
            ledgerEntry.GLBatchId = stockBatchId;

            // 6. Save Everything
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> ProcessStockEntryBatchAsync(
            Guid companyId, StockEntryType entryType, Guid vendorId, string reference,
            IReadOnlyCollection<StockEntryBatchLineDto> lines, string userId)
        {
            var validLines = lines.Where(x => x.ItemId != Guid.Empty).ToList();
            if (validLines.Count == 0) return "Add at least one item line.";

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                Vendor? vendor = null;
                if (entryType == StockEntryType.DirectReceipt)
                {
                    vendor = await ctx.Vendors.FirstOrDefaultAsync(x => x.Id == vendorId && x.CompanyId == companyId);
                    if (vendor?.PayablesAccountId == null || vendor.PayablesAccountId == Guid.Empty)
                        return "Select a vendor with a configured Payables account.";
                }

                var glLines = new List<GLJournalLine>();
                var stagedMovements = new List<StockLedger>();
                var quantitiesOnHand = new Dictionary<Guid, decimal>();
                var warehouseQuantitiesOnHand = new Dictionary<(Guid ItemId, Guid WarehouseId), decimal>();
                var now = DateTime.UtcNow;
                var receiptRef = string.IsNullOrWhiteSpace(reference) ? entryType.ToString() : reference.Trim();

                foreach (var line in validLines)
                {
                    var item = await ctx.Items.FirstOrDefaultAsync(x => x.Id == line.ItemId && x.CompanyId == companyId);
                    if (item == null) return $"Item {line.ItemId} was not found in this company.";
                    if (line.ProjectId.HasValue && !await ctx.Projects.AnyAsync(p => p.Id == line.ProjectId && p.CompanyId == companyId && p.IsAccountingProject))
                        return $"Selected project for '{item.Name}' is unavailable.";

                    if (entryType == StockEntryType.DirectReceipt)
                    {
                        if (!item.IsService && (line.WarehouseId == Guid.Empty || !await ctx.Warehouses.AnyAsync(w => w.Id == line.WarehouseId && w.CompanyId == companyId)))
                            return $"Select a valid warehouse for '{item.Name}'.";
                        if (line.TotalValueChange <= 0) return $"Receipt cost for '{item.Name}' must be greater than zero.";

                        if (item.IsService)
                        {
                            if (item.CostOfGoodsSoldAccountId == Guid.Empty) return $"Service '{item.Name}' has no expense GL account.";
                            glLines.Add(new GLJournalLine { SegCoaId = item.CostOfGoodsSoldAccountId, Debit = line.TotalValueChange, ProjectId = line.ProjectId, Reference = $"Service Exp: {item.Name}" });
                        }
                        else
                        {
                            if (line.Quantity <= 0) return $"Receipt quantity for '{item.Name}' must be greater than zero.";
                            if (!quantitiesOnHand.TryGetValue(item.Id, out var quantityOnHand))
                            {
                                quantityOnHand = Math.Max(0m, await ctx.StockLedgers
                                    .Where(x => x.CompanyId == companyId && x.ItemId == item.Id)
                                    .SumAsync(x => x.QuantityChanged));
                                quantitiesOnHand[item.Id] = quantityOnHand;
                            }
                            var oldWacc = item.WeightedAverageCost;
                            var newQuantity = quantityOnHand + line.Quantity;
                            item.WeightedAverageCost = newQuantity > 0
                                ? ((quantityOnHand * oldWacc) + line.TotalValueChange) / newQuantity
                                : oldWacc;
                            quantitiesOnHand[item.Id] = newQuantity;
                            ctx.ItemCostHistories.Add(new ItemCostHistory
                            {
                                ItemId = item.Id, OldQty = quantityOnHand, OldWacc = oldWacc,
                                NewQtyIn = line.Quantity, NewCostIn = line.TotalValueChange / line.Quantity,
                                ResultingWacc = item.WeightedAverageCost, Reference = receiptRef, DateChanged = now
                            });
                            var movement = new StockLedger
                            {
                                CompanyId = companyId, ItemId = item.Id, WarehouseId = line.WarehouseId,
                                QuantityChanged = line.Quantity, UomId = line.UomId,
                                UomName = line.UomName, UomConversionFactor = UomConversion.NormalizeFactor(line.UomConversionFactor),
                                QuantityInUom = line.QuantityInUom > 0 ? line.QuantityInUom : UomConversion.FromBase(line.Quantity, line.UomConversionFactor),
                                Type = StockMovementType.Purchase, CostAtTime = item.WeightedAverageCost,
                                Reference = receiptRef, Date = now, ProjectId = line.ProjectId
                            };
                            ctx.StockLedgers.Add(movement);
                            stagedMovements.Add(movement);
                            if (item.InventoryAssetAccountId == Guid.Empty) return $"Item '{item.Name}' has no Inventory Asset account.";
                            glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = line.TotalValueChange, ProjectId = line.ProjectId, Reference = $"Stock In: {item.Name}" });
                        }
                        glLines.Add(new GLJournalLine { SegCoaId = vendor!.PayablesAccountId!.Value, Credit = line.TotalValueChange, ProjectId = line.ProjectId, Reference = $"Bill: {vendor.Name}" });
                        continue;
                    }

                    if (item.IsService) return $"Cannot adjust stock for service item '{item.Name}'.";
                    if (line.WarehouseId == Guid.Empty || !await ctx.Warehouses.AnyAsync(w => w.Id == line.WarehouseId && w.CompanyId == companyId))
                        return $"Select a valid warehouse for '{item.Name}'.";
                    if (!quantitiesOnHand.TryGetValue(item.Id, out var currentQty))
                    {
                        currentQty = Math.Max(0m, await ctx.StockLedgers
                            .Where(x => x.CompanyId == companyId && x.ItemId == item.Id)
                            .SumAsync(x => x.QuantityChanged));
                        quantitiesOnHand[item.Id] = currentQty;
                    }
                    var oldCost = item.WeightedAverageCost;
                    var oldValue = currentQty * oldCost;
                    var qtyChange = 0m;
                    var valueChange = 0m;
                    switch (entryType)
                    {
                        case StockEntryType.QuantityIncrease: qtyChange = Math.Abs(line.Quantity); break;
                        case StockEntryType.QuantityDecrease: qtyChange = -Math.Abs(line.Quantity); break;
                        case StockEntryType.CostIncrease: valueChange = Math.Abs(line.TotalValueChange); break;
                        case StockEntryType.CostDecrease: valueChange = -Math.Abs(line.TotalValueChange); break;
                        case StockEntryType.BothIncrease: qtyChange = Math.Abs(line.Quantity); valueChange = Math.Abs(line.TotalValueChange); break;
                        case StockEntryType.BothDecrease: qtyChange = -Math.Abs(line.Quantity); valueChange = -Math.Abs(line.TotalValueChange); break;
                        default: return "Unsupported stock adjustment type.";
                    }
                    if (qtyChange != 0)
                    {
                        var warehouseKey = (item.Id, line.WarehouseId);
                        if (!warehouseQuantitiesOnHand.TryGetValue(warehouseKey, out var warehouseQty))
                        {
                            warehouseQty = await ctx.StockLedgers
                                .Where(x => x.CompanyId == companyId && x.ItemId == item.Id && x.WarehouseId == line.WarehouseId)
                                .SumAsync(x => x.QuantityChanged);
                        }

                        if (warehouseQty + qtyChange < 0)
                            return $"Cannot reduce '{item.Name}' below zero stock in the selected warehouse ({warehouseQty:N4} available).";

                        warehouseQuantitiesOnHand[warehouseKey] = warehouseQty + qtyChange;
                    }
                    if (currentQty + qtyChange < 0) return $"Cannot reduce '{item.Name}' below zero stock ({currentQty:N4} available).";
                    if (oldValue + valueChange < 0) return $"Cannot reduce '{item.Name}' valuation below zero.";
                    if (valueChange != 0 && (item.AdjustmentExpenseAccountId == Guid.Empty || item.InventoryAssetAccountId == Guid.Empty))
                        return $"Item '{item.Name}' needs Inventory Asset and Adjustment Expense accounts for value adjustments.";

                    var newQty = currentQty + qtyChange;
                    item.WeightedAverageCost = newQty > 0 ? (oldValue + valueChange) / newQty : 0;
                    quantitiesOnHand[item.Id] = newQty;
                    ctx.ItemCostHistories.Add(new ItemCostHistory
                    {
                        ItemId = item.Id, OldQty = currentQty, OldWacc = oldCost, NewQtyIn = qtyChange,
                        NewCostIn = qtyChange != 0 ? valueChange / qtyChange : valueChange,
                        ResultingWacc = item.WeightedAverageCost, Reference = receiptRef, DateChanged = now
                    });
                    if (qtyChange != 0)
                    {
                        var signedUomQty = entryType == StockEntryType.QuantityDecrease || entryType == StockEntryType.BothDecrease
                            ? -Math.Abs(line.QuantityInUom) : Math.Abs(line.QuantityInUom);
                        var movement = new StockLedger
                        {
                            CompanyId = companyId, ItemId = item.Id, WarehouseId = line.WarehouseId,
                            QuantityChanged = qtyChange, UomId = line.UomId, UomName = line.UomName,
                            UomConversionFactor = UomConversion.NormalizeFactor(line.UomConversionFactor),
                            QuantityInUom = line.QuantityInUom == 0 ? UomConversion.FromBase(qtyChange, line.UomConversionFactor) : signedUomQty,
                            Type = StockMovementType.Adjustment, CostAtTime = item.WeightedAverageCost,
                            Reference = receiptRef, Date = now, ProjectId = line.ProjectId
                        };
                        ctx.StockLedgers.Add(movement);
                        stagedMovements.Add(movement);
                    }
                    if (valueChange > 0)
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = valueChange, ProjectId = line.ProjectId, Reference = $"Adj In: {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = item.AdjustmentExpenseAccountId, Credit = valueChange, ProjectId = line.ProjectId, Reference = $"Adj Gain: {item.Name}" });
                    }
                    else if (valueChange < 0)
                    {
                        var loss = Math.Abs(valueChange);
                        glLines.Add(new GLJournalLine { SegCoaId = item.AdjustmentExpenseAccountId, Debit = loss, ProjectId = line.ProjectId, Reference = $"Adj Loss: {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Credit = loss, ProjectId = line.ProjectId, Reference = $"Adj Out: {item.Name}" });
                    }
                }

                Guid? batchId = null;
                if (glLines.Count > 0)
                {
                    var (error, createdBatchId) = await _glOps.CreateJournalEntryAsync(
                        companyId, DateOnly.FromDateTime(DateTime.Today), "Stock Entry", receiptRef, glLines, userId);
                    if (!string.IsNullOrWhiteSpace(error)) throw new Exception(error);
                    if (!createdBatchId.HasValue) throw new Exception("GL review batch was not created.");
                    var postError = await _glOps.PostBatchAsync(companyId, createdBatchId.Value, userId);
                    if (!string.IsNullOrWhiteSpace(postError)) throw new Exception(postError);
                    batchId = createdBatchId;
                    foreach (var movement in stagedMovements) movement.GLBatchId = batchId;
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Stock entry batch failed: {ex.Message}";
            }
        }

        // 2. SHIP TRANSFER (Auto-Posting to GL)
        public async Task<string> ShipTransferAsync(Guid companyId, Guid itemId, Guid fromWhId, Guid toWhId, decimal qtyInUom, Guid transitAccountId, string note, string userId, Guid? uomId = null, string? uomName = null, decimal uomConversionFactor = 1m)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync(); // Added Transaction Safety

            try
            {
                var item = await ctx.Items.FindAsync(itemId);
                if (item == null) return "Item not found.";

                var itemUomFactors = await ctx.ItemUomConversionLines
                    .Where(x => x.ItemId == itemId && x.IsActive)
                    .ToDictionaryAsync(x => x.UomId, x => x.ConversionFactorToBase);
                var conversionMap = await ctx.UomConversionRules
                    .Where(x => x.CompanyId == companyId && x.IsActive)
                    .ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);

                if (uomId.HasValue && uomId != item.UomId
                    && !itemUomFactors.ContainsKey(uomId.Value)
                    && (!item.UomId.HasValue || !UomConversion.FactorBetween(item.UomId.Value, uomId.Value, conversionMap).HasValue))
                    return "The selected UOM is not configured for this item.";

                decimal expectedFactor = UomConversion.FactorFor(item, uomId, itemUomFactors, conversionMap);
                if (uomId.HasValue && Math.Abs(UomConversion.NormalizeFactor(uomConversionFactor) - expectedFactor) > 0.0001m)
                    return "The selected UOM conversion is invalid or outdated. Reload the item and try again.";

                decimal qty = UomConversion.ToBase(qtyInUom, expectedFactor);
                if (qty <= 0) return "Transfer quantity must be greater than zero.";

                decimal available = await GetStockLevel(itemId, fromWhId);
                if (available < qty) return $"Insufficient stock. Available: {available} {item.UoM}";

                string resolvedUomName = string.IsNullOrWhiteSpace(uomName)
                    ? (uomId == item.UomId ? item.UoM : "UOM")
                    : uomName.Trim();

                var transfer = new StockTransfer
                {
                    CompanyId = companyId,
                    ItemId = itemId,
                    FromWarehouseId = fromWhId,
                    ToWarehouseId = toWhId,
                    Quantity = qty,
                    UomId = uomId ?? item.UomId,
                    UomName = resolvedUomName,
                    UomConversionFactor = expectedFactor,
                    QuantityInUom = qtyInUom,
                    Status = TransferStatus.InTransit,
                    DateShipped = DateTime.UtcNow,
                    TransitGLAccountId = transitAccountId,
                    ValueAtShipment = qty * item.WeightedAverageCost,
                    Reference = note
                };
                ctx.StockTransfers.Add(transfer);

                var outboundMovement = new StockLedger
                {
                    CompanyId = companyId,
                    ItemId = itemId,
                    WarehouseId = fromWhId,
                    QuantityChanged = -qty,
                    UomId = transfer.UomId,
                    UomName = transfer.UomName,
                    UomConversionFactor = transfer.UomConversionFactor,
                    QuantityInUom = -transfer.QuantityInUom,
                    Type = StockMovementType.TransferOut,
                    CostAtTime = item.WeightedAverageCost,
                    Reference = $"SHIP: {note}"
                };
                ctx.StockLedgers.Add(outboundMovement);

                var glLines = new List<GLJournalLine>
                {
                    new() { SegCoaId  = transitAccountId, Debit = transfer.ValueAtShipment, Credit = 0, Reference = "Transit" },
                    new() { SegCoaId  = item.InventoryAssetAccountId, Debit = 0, Credit = transfer.ValueAtShipment, Reference = "Shipment" }
                };

                // Create GL Batch
                var (glErr, batchId) = await _glOps.CreateJournalEntryAsync(companyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Ship", note, glLines, userId);
                if (!string.IsNullOrEmpty(glErr)) throw new Exception(glErr);
                if (!batchId.HasValue) throw new Exception("Transfer shipment did not create a GL review batch.");

                // Auto-Post GL Batch
                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");
                    transfer.GLBatchId = batchId;
                    outboundMovement.GLBatchId = batchId;
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

        public async Task<string> ShipTransferBatchAsync(
            Guid companyId, Guid fromWarehouseId, Guid toWarehouseId, Guid transitAccountId,
            string reference, IReadOnlyCollection<StockTransferBatchLineDto> inputLines, string userId)
        {
            var lines = inputLines.Where(x => x.ItemId != Guid.Empty).ToList();
            if (lines.Count == 0) return "Add at least one item line to the transfer.";
            if (fromWarehouseId == toWarehouseId) return "Source and destination warehouses must be different.";

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var validWarehouses = await ctx.Warehouses.AnyAsync(w => w.Id == fromWarehouseId && w.CompanyId == companyId)
                    && await ctx.Warehouses.AnyAsync(w => w.Id == toWarehouseId && w.CompanyId == companyId);
                if (!validWarehouses) return "Select valid source and destination warehouses.";

                var conversionMap = await ctx.UomConversionRules.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.IsActive)
                    .ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
                var itemFactors = await ctx.ItemUomConversionLines.AsNoTracking()
                    .Where(x => x.IsActive && x.Item!.CompanyId == companyId)
                    .GroupBy(x => new { x.ItemId, x.UomId })
                    .ToDictionaryAsync(g => (g.Key.ItemId, g.Key.UomId), g => g.First().ConversionFactorToBase);

                var transferLines = new List<StockTransferLine>();
                var outboundMovements = new List<StockLedger>();
                var glLines = new List<GLJournalLine>();
                var availableByItem = new Dictionary<Guid, decimal>();
                foreach (var input in lines)
                {
                    var item = await ctx.Items.FirstOrDefaultAsync(x => x.Id == input.ItemId && x.CompanyId == companyId);
                    if (item == null) return $"Item {input.ItemId} was not found in this company.";
                    if (item.IsService) return $"Service item '{item.Name}' cannot be transferred.";
                    if (input.ProjectId.HasValue && !await ctx.Projects.AnyAsync(p => p.Id == input.ProjectId && p.CompanyId == companyId && p.IsAccountingProject))
                        return $"Selected project for '{item.Name}' is unavailable.";

                    var factors = itemFactors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                    if (input.UomId.HasValue && input.UomId != item.UomId
                        && !factors.ContainsKey(input.UomId.Value)
                        && (!item.UomId.HasValue || !UomConversion.FactorBetween(item.UomId.Value, input.UomId.Value, conversionMap).HasValue))
                        return $"The selected UOM is not configured for '{item.Name}'.";
                    var expectedFactor = UomConversion.FactorFor(item, input.UomId, factors, conversionMap);
                    if (input.UomId.HasValue && Math.Abs(UomConversion.NormalizeFactor(input.UomConversionFactor) - expectedFactor) > 0.0001m)
                        return $"The selected UOM conversion for '{item.Name}' is invalid or outdated.";
                    if (input.QuantityInUom <= 0) return $"Transfer quantity for '{item.Name}' must be greater than zero.";
                    var baseQty = UomConversion.ToBase(input.QuantityInUom, expectedFactor);
                    if (!availableByItem.TryGetValue(item.Id, out var available))
                    {
                        available = await GetStockLevel(item.Id, fromWarehouseId);
                        availableByItem[item.Id] = available;
                    }
                    if (available < baseQty) return $"Insufficient stock for '{item.Name}'. Available: {available:N4} {item.UoM}.";
                    availableByItem[item.Id] = available - baseQty;

                    var transferLine = new StockTransferLine
                    {
                        ItemId = item.Id, Item = item, Quantity = baseQty,
                        UomId = input.UomId ?? item.UomId,
                        UomName = string.IsNullOrWhiteSpace(input.UomName) ? item.UoM : input.UomName.Trim(),
                        UomConversionFactor = expectedFactor,
                        QuantityInUom = input.QuantityInUom,
                        ValueAtShipment = baseQty * item.WeightedAverageCost,
                        ProjectId = input.ProjectId
                    };
                    transferLines.Add(transferLine);
                    var movement = new StockLedger
                    {
                        CompanyId = companyId, ItemId = item.Id, WarehouseId = fromWarehouseId,
                        QuantityChanged = -baseQty, UomId = transferLine.UomId, UomName = transferLine.UomName,
                        UomConversionFactor = expectedFactor, QuantityInUom = -input.QuantityInUom,
                        Type = StockMovementType.TransferOut, CostAtTime = item.WeightedAverageCost,
                        Reference = $"SHIP: {reference}", Date = DateTime.UtcNow, ProjectId = input.ProjectId
                    };
                    outboundMovements.Add(movement);
                    if (item.InventoryAssetAccountId == Guid.Empty) return $"Item '{item.Name}' has no Inventory Asset account.";
                    glLines.Add(new GLJournalLine { SegCoaId = transitAccountId, Debit = transferLine.ValueAtShipment, ProjectId = input.ProjectId, Reference = $"Transit: {item.Name}" });
                    glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Credit = transferLine.ValueAtShipment, ProjectId = input.ProjectId, Reference = $"Shipment: {item.Name}" });
                }

                if (string.IsNullOrWhiteSpace(reference)) reference = $"TRF-{DateTime.UtcNow:yyyyMMddHHmmss}";
                var first = transferLines[0];
                var transfer = new StockTransfer
                {
                    CompanyId = companyId, Reference = reference.Trim(), FromWarehouseId = fromWarehouseId,
                    ToWarehouseId = toWarehouseId, ItemId = first.ItemId, Quantity = first.Quantity,
                    UomId = first.UomId, UomName = first.UomName, UomConversionFactor = first.UomConversionFactor,
                    QuantityInUom = first.QuantityInUom, Status = TransferStatus.InTransit,
                    DateShipped = DateTime.UtcNow, TransitGLAccountId = transitAccountId,
                    ValueAtShipment = transferLines.Sum(x => x.ValueAtShipment), HasLineDetails = true,
                    Lines = transferLines
                };
                foreach (var line in transfer.Lines) line.StockTransferId = transfer.Id;
                ctx.StockTransfers.Add(transfer);
                foreach (var movement in outboundMovements) ctx.StockLedgers.Add(movement);

                var (glError, batchId) = await _glOps.CreateJournalEntryAsync(
                    companyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Ship", transfer.Reference, glLines, userId);
                if (!string.IsNullOrWhiteSpace(glError)) throw new Exception(glError);
                if (!batchId.HasValue) throw new Exception("Transfer shipment did not create a GL review batch.");
                var postingError = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                if (!string.IsNullOrWhiteSpace(postingError)) throw new Exception(postingError);
                transfer.GLBatchId = batchId;
                foreach (var movement in outboundMovements) movement.GLBatchId = batchId;

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

        public async Task<string> ReceiveTransferBatchAsync(
            Guid transferId, IReadOnlyCollection<StockTransferReceiveLineDto> receivedLines, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var transfer = await ctx.StockTransfers.Include(x => x.Lines).ThenInclude(x => x.Item)
                    .FirstOrDefaultAsync(x => x.Id == transferId && x.HasLineDetails);
                if (transfer == null) return "Multi-line stock transfer not found.";
                if (transfer.Status != TransferStatus.InTransit) return "This transfer has already been received or cancelled.";
                if (transfer.GLBatchId.HasValue && await ctx.GLBatches.AsNoTracking().AnyAsync(x => x.Id == transfer.GLBatchId && x.CompanyId == transfer.CompanyId && x.Status != BatchStatus.Posted))
                    return "Transfer cannot be received until its shipment batch has been posted.";
                if (receivedLines.Count != transfer.Lines.Count || receivedLines.Any(x => transfer.Lines.All(y => y.Id != x.StockTransferLineId)))
                    return "Provide a receipt quantity for every transfer line.";

                var prefix = $"RECV:{transfer.Id:N}|";
                if (await ctx.StockLedgers.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == transfer.CompanyId && x.Type == StockMovementType.TransferIn && x.Reference.StartsWith(prefix)
                    && x.GLBatchId.HasValue && ctx.GLBatches.Any(b => b.Id == x.GLBatchId.Value && (b.Status == BatchStatus.Draft || b.Status == BatchStatus.Ready))))
                    return "This transfer receipt is already awaiting GL review.";

                var glLines = new List<GLJournalLine>();
                var inboundMovements = new List<StockLedger>();
                decimal totalReceivedBase = 0;
                foreach (var detail in transfer.Lines)
                {
                    var received = receivedLines.First(x => x.StockTransferLineId == detail.Id).QuantityReceived;
                    var transactionQty = detail.QuantityInUom > 0 ? detail.QuantityInUom : detail.Quantity;
                    if (received <= 0 || received > transactionQty)
                        return $"Receipt quantity for '{detail.Item?.Name}' must be above zero and no more than {transactionQty:N4} {detail.UomName}.";
                    var factor = UomConversion.NormalizeFactor(detail.UomConversionFactor);
                    var actualBaseQty = UomConversion.ToBase(received, factor);
                    var unitCost = detail.Quantity > 0 ? detail.ValueAtShipment / detail.Quantity : detail.Item?.WeightedAverageCost ?? 0;
                    var receivedValue = actualBaseQty * unitCost;
                    var lostValue = detail.ValueAtShipment - receivedValue;
                    var reference = $"{prefix}{detail.Id:N}|{transfer.Reference}";
                    inboundMovements.Add(new StockLedger
                    {
                        CompanyId = transfer.CompanyId, ItemId = detail.ItemId, WarehouseId = transfer.ToWarehouseId,
                        QuantityChanged = actualBaseQty, UomId = detail.UomId, UomName = detail.UomName,
                        UomConversionFactor = factor, QuantityInUom = received, Type = StockMovementType.TransferIn,
                        CostAtTime = unitCost, Reference = reference, Date = DateTime.UtcNow, ProjectId = detail.ProjectId
                    });
                    if (detail.Item == null || detail.Item.InventoryAssetAccountId == Guid.Empty)
                        return $"Item '{detail.Item?.Name ?? detail.ItemId.ToString()}' has no Inventory Asset account.";
                    glLines.Add(new GLJournalLine { SegCoaId = transfer.TransitGLAccountId, Credit = detail.ValueAtShipment, ProjectId = detail.ProjectId, Reference = $"Clear Transit: {detail.Item.Name}" });
                    glLines.Add(new GLJournalLine { SegCoaId = detail.Item.InventoryAssetAccountId, Debit = receivedValue, ProjectId = detail.ProjectId, Reference = $"Receipt: {detail.Item.Name}" });
                    if (lostValue > 0)
                    {
                        if (detail.Item.AdjustmentExpenseAccountId == Guid.Empty) return $"Item '{detail.Item.Name}' needs an Adjustment Expense account for transit loss.";
                        glLines.Add(new GLJournalLine { SegCoaId = detail.Item.AdjustmentExpenseAccountId, Debit = lostValue, ProjectId = detail.ProjectId, Reference = $"Transit Loss: {detail.Item.Name}" });
                    }
                    detail.QuantityReceived = actualBaseQty;
                    totalReceivedBase += actualBaseQty;
                }

                foreach (var movement in inboundMovements) ctx.StockLedgers.Add(movement);
                var (glError, batchId) = await _glOps.CreateJournalEntryAsync(
                    transfer.CompanyId, DateOnly.FromDateTime(DateTime.Today), "Transfer Recv", transfer.Reference, glLines, userId);
                if (!string.IsNullOrWhiteSpace(glError)) throw new Exception(glError);
                if (!batchId.HasValue) throw new Exception("Transfer receipt did not create a GL review batch.");
                foreach (var movement in inboundMovements) movement.GLBatchId = batchId;
                var postingError = await _glOps.PostBatchAsync(transfer.CompanyId, batchId.Value, userId);
                if (!string.IsNullOrWhiteSpace(postingError)) throw new Exception(postingError);
                transfer.Status = TransferStatus.Received;
                transfer.DateReceived = DateTime.UtcNow;
                transfer.QuantityReceived = totalReceivedBase;
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

        // 4. RECEIVE TRANSFER (Auto-Posting to GL)
        public async Task<string> ReceiveTransferAsync(Guid transferId, decimal actualQtyReceived, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync(); // Added Transaction Safety

            try
            {
                var transfer = await ctx.StockTransfers.Include(t => t.Item).FirstOrDefaultAsync(t => t.Id == transferId);
                if (transfer == null) return "Transfer not found.";

                if (transfer.Status != TransferStatus.InTransit) return "This transfer has already been received or cancelled.";
                if (transfer.GLBatchId.HasValue)
                {
                    var shipmentBatchStatus = await ctx.GLBatches.AsNoTracking()
                        .Where(batch => batch.Id == transfer.GLBatchId.Value && batch.CompanyId == transfer.CompanyId)
                        .Select(batch => (BatchStatus?)batch.Status)
                        .FirstOrDefaultAsync();
                    if (shipmentBatchStatus != BatchStatus.Posted)
                        return "Transfer cannot be received until its shipment batch has been posted.";
                }
                var receiptReference = $"RECV:{transfer.Id:N}|{transfer.Reference}";
                bool receiptAwaitingReview = await ctx.StockLedgers.IgnoreQueryFilters()
                    .AnyAsync(movement => movement.CompanyId == transfer.CompanyId
                        && movement.Type == StockMovementType.TransferIn
                        && movement.Reference == receiptReference
                        && movement.GLBatchId.HasValue
                        && ctx.GLBatches.Any(batch => batch.Id == movement.GLBatchId.Value
                            && (batch.Status == BatchStatus.Draft || batch.Status == BatchStatus.Ready)));
                if (receiptAwaitingReview)
                    return "This transfer receipt is already awaiting GL review.";
                if (actualQtyReceived <= 0) return "Receipt quantity must be greater than zero.";

                decimal transactionQty = transfer.QuantityInUom > 0 ? transfer.QuantityInUom : transfer.Quantity;
                if (actualQtyReceived > transactionQty)
                    return $"Cannot receive more than shipped ({transactionQty:N4} {transfer.UomName}).";

                decimal transferFactor = UomConversion.NormalizeFactor(transfer.UomConversionFactor);
                decimal actualBaseQty = UomConversion.ToBase(actualQtyReceived, transferFactor);

                var inboundMovement = new StockLedger
                {
                    CompanyId = transfer.CompanyId,
                    ItemId = transfer.ItemId,
                    WarehouseId = transfer.ToWarehouseId,
                    QuantityChanged = actualBaseQty,
                    UomId = transfer.UomId,
                    UomName = transfer.UomName,
                    UomConversionFactor = transferFactor,
                    QuantityInUom = actualQtyReceived,
                    Type = StockMovementType.TransferIn,
                    CostAtTime = transfer.Quantity > 0 ? transfer.ValueAtShipment / transfer.Quantity : transfer.Item.WeightedAverageCost,
                    Reference = receiptReference
                };
                ctx.StockLedgers.Add(inboundMovement);

                decimal totalShippedValue = transfer.ValueAtShipment;
                decimal shipmentUnitCost = transfer.Quantity > 0
                    ? transfer.ValueAtShipment / transfer.Quantity
                    : transfer.Item.WeightedAverageCost;
                decimal receivedValue = actualBaseQty * shipmentUnitCost;
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
                if (!batchId.HasValue) throw new Exception("Transfer receipt did not create a GL review batch.");

                // Auto-Post GL Batch
                if (batchId.HasValue)
                {
                    inboundMovement.GLBatchId = batchId;
                    var postErr = await _glOps.PostBatchAsync(transfer.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");
                    var receiptBatchStatus = await ctx.GLBatches.AsNoTracking()
                        .Where(batch => batch.Id == batchId.Value && batch.CompanyId == transfer.CompanyId)
                        .Select(batch => (BatchStatus?)batch.Status)
                        .FirstOrDefaultAsync();
                    if (receiptBatchStatus == BatchStatus.Posted)
                    {
                        transfer.Status = TransferStatus.Received;
                        transfer.DateReceived = DateTime.UtcNow;
                        transfer.QuantityReceived = actualBaseQty;
                    }
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

        public async Task<string> AdjustStockAsync(Guid companyId, Guid itemId, Guid warehouseId, StockEntryType adjType, decimal qty, decimal totalValueChange, string reference, string userId, Guid? uomId = null, string? uomName = null, decimal uomConversionFactor = 1m, decimal quantityInUom = 0m)
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
                StockLedger? movementEntry = null;
                if (qtyChange != 0)
                {
                    movementEntry = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        ItemId = itemId,
                        WarehouseId = warehouseId,
                        QuantityChanged = qtyChange,
                        UomId = uomId,
                        UomName = uomName ?? string.Empty,
                        UomConversionFactor = UomConversion.NormalizeFactor(uomConversionFactor),
                        QuantityInUom = quantityInUom == 0m ? UomConversion.FromBase(qtyChange, uomConversionFactor) : quantityInUom,
                        Type = StockMovementType.Adjustment,
                        CostAtTime = item.WeightedAverageCost,
                        Reference = reference,
                        Date = DateTime.UtcNow
                    };
                    ctx.StockLedgers.Add(movementEntry);
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
                    if (!batchId.HasValue) throw new Exception("GL staging did not create a review batch for this inventory adjustment.");
                    var postError = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postError)) throw new Exception($"GL posting error: {postError}");
                    if (movementEntry != null) movementEntry.GLBatchId = batchId;
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
                .Include(t => t.Lines).ThenInclude(line => line.Item)
                .Where(t => t.CompanyId == companyId && t.Status == TransferStatus.InTransit
                    && (!t.GLBatchId.HasValue || ctx.GLBatches.Any(batch => batch.Id == t.GLBatchId.Value
                        && batch.CompanyId == companyId && batch.Status == BatchStatus.Posted)))
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
                var stagedMovements = new List<StockLedger>();
                var quantitiesOnHand = new Dictionary<Guid, decimal>();
                var warehouseQuantitiesOnHand = new Dictionary<(Guid ItemId, Guid WarehouseId), decimal>();

                foreach (var line in validLines)
                {
                    var item = await ctx.Items.FirstOrDefaultAsync(x => x.Id == line.ItemId && x.CompanyId == companyId);
                    if (item == null) return $"Item with ID '{line.ItemId}' could not be resolved.";
                    if (item.IsService) return $"STOP: '{item.Name}' is a service item. Physical inventory parameters cannot be adjusted.";
                    if (item.InventoryAssetAccountId == Guid.Empty) return $"CONFIGURATION ERROR: '{item.Name}' is missing an Inventory Asset GL Account mapping.";
                    if (line.ProjectId.HasValue && !await ctx.Projects.AnyAsync(p => p.Id == line.ProjectId && p.CompanyId == companyId && p.IsAccountingProject))
                        return $"Selected project for '{item.Name}' is unavailable.";
                    if (line.QuantityChange != 0 && (line.WarehouseId == Guid.Empty ||
                        !await ctx.Warehouses.AnyAsync(w => w.Id == line.WarehouseId && w.CompanyId == companyId)))
                        return $"A valid warehouse is required for the quantity adjustment of '{item.Name}'.";

                    // 1. Resolve Current Quantities Across All Warehouses for WACC Tracking
                    if (!quantitiesOnHand.TryGetValue(item.Id, out var currentTotalQty))
                    {
                        currentTotalQty = await ctx.StockLedgers
                            .Where(s => s.CompanyId == companyId && s.ItemId == item.Id)
                            .SumAsync(s => s.QuantityChanged);
                        currentTotalQty = Math.Max(0m, currentTotalQty);
                    }

                    decimal oldWacc = item.WeightedAverageCost;
                    decimal oldValuation = currentTotalQty * oldWacc;

                    // 2. Handle Directional Financial Ledger Adjustments
                    decimal finalLineCostChange = Math.Abs(line.TotalValueChange);

                    if (line.IsDebitInventory)
                    {
                        // Debit Inventory Asset (Asset Up), Credit Balancing Account
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = finalLineCostChange, Credit = 0, ProjectId = line.ProjectId, Reference = $"Inv Adj Dr - {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = balancingGlAccountId, Debit = 0, Credit = finalLineCostChange, ProjectId = line.ProjectId, Reference = $"Inv Adj Bal Cr - {item.Name}" });
                    }
                    else
                    {
                        // Credit Inventory Asset (Asset Down), Debit Balancing Account
                        glLines.Add(new GLJournalLine { SegCoaId = balancingGlAccountId, Debit = finalLineCostChange, Credit = 0, ProjectId = line.ProjectId, Reference = $"Inv Adj Bal Dr - {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = 0, Credit = finalLineCostChange, ProjectId = line.ProjectId, Reference = $"Inv Adj Cr - {item.Name}" });
                    }

                    // 3. Compute Stock Valuation Shifts
                    decimal absoluteValueShift = line.IsDebitInventory ? finalLineCostChange : -finalLineCostChange;
                    decimal newTotalQty = currentTotalQty + line.QuantityChange;
                    decimal newValuation = oldValuation + absoluteValueShift;

                    if (line.QuantityChange != 0)
                    {
                        var warehouseKey = (item.Id, line.WarehouseId);
                        if (!warehouseQuantitiesOnHand.TryGetValue(warehouseKey, out var warehouseQty))
                        {
                            warehouseQty = await ctx.StockLedgers
                                .Where(s => s.CompanyId == companyId && s.ItemId == item.Id && s.WarehouseId == line.WarehouseId)
                                .SumAsync(s => s.QuantityChanged);
                        }

                        if (warehouseQty + line.QuantityChange < 0)
                            return $"VALUATION BLOCKED: Adjustment would reduce '{item.Name}' below zero in the selected warehouse ({warehouseQty:N4} available).";

                        warehouseQuantitiesOnHand[warehouseKey] = warehouseQty + line.QuantityChange;
                    }

                    if (newTotalQty < 0) return $"VALUATION BLOCKED: Adjustment forces absolute quantity of '{item.Name}' below zero to ({newTotalQty}). Transaction aborted.";
                    if (newValuation < 0) return $"VALUATION BLOCKED: Adjustment forces the value of '{item.Name}' below zero. Transaction aborted.";

                    // Update WACC values natively if stock balances remain positive
                    item.WeightedAverageCost = newTotalQty > 0 ? Math.Round(newValuation / newTotalQty, 4) : 0;
                    quantitiesOnHand[item.Id] = newTotalQty;

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
                        var movement = new StockLedger
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = companyId,
                            ItemId = item.Id,
                            WarehouseId = line.WarehouseId,
                            QuantityChanged = line.QuantityChange,
                            Type = StockMovementType.Adjustment,
                            CostAtTime = item.WeightedAverageCost,
                            Reference = $"ADJ-{postingDate:yyMMdd}",
                            Date = DateTime.UtcNow,
                            ProjectId = line.ProjectId
                        };
                        ctx.StockLedgers.Add(movement);
                        stagedMovements.Add(movement);
                    }
                }

                // 6. Pack entries into general ledger batches
                string batchRefName = $"INVADJ-{DateTime.UtcNow:yyMMddHHmm}";
                var (glError, batchId) = await _glOps.CreateJournalEntryAsync(companyId, postingDate, batchRefName, "Inventory Batch Sub-ledger Adjustment", glLines, userId);
                if (!string.IsNullOrEmpty(glError)) throw new Exception(glError);
                if (!batchId.HasValue) throw new Exception("GL staging did not create a review batch for this inventory adjustment.");

                var postError = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                if (!string.IsNullOrEmpty(postError)) throw new Exception(postError);
                foreach (var movement in stagedMovements)
                    movement.GLBatchId = batchId;

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
