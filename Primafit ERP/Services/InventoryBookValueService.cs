using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed record InventoryBookPosition(Guid ItemId, Guid WarehouseId, decimal Quantity, decimal Value, DateTime LastMovement)
{
    public decimal UnitCost => Quantity == 0 ? 0 : Value / Quantity;
}

/// <summary>Historical carrying value from accepted movements, never today's item master cost.</summary>
public static class InventoryBookValueService
{
    public static async Task<List<InventoryBookPosition>> GetAsync(AppDbContext db, Guid companyId, DateOnly asOf, Guid? warehouseId = null)
    {
        // Movement dates identify the economic event; batch reuse must not
        // assign a later movement the date of an older journal in the same batch.
        var ledgerDates = await ReportAccounting.PostedTransactions(db, companyId).ToListAsync();
        var acceptedBatches = ledgerDates.Select(x => x.BatchId).Distinct().ToList();
        // Recover shared posting values across all warehouses before applying a
        // location filter, so a selected location cannot inherit the whole batch.
        var movements = await db.StockLedgers.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId
            && (!x.GLBatchId.HasValue || x.CommittedAt.HasValue || (x.ValueChanged == null && acceptedBatches.Contains(x.GLBatchId.Value)) || db.GLBatches.Any(b => b.Id == x.GLBatchId && b.CompanyId == companyId && b.Status == BatchStatus.Posted))).ToListAsync();
        DateOnly BookDate(StockLedger movement)
        {
            var physicalDate = DateOnly.FromDateTime(movement.Date);
            if (!movement.GLBatchId.HasValue) return physicalDate;
            var candidates = ledgerDates.Where(x => x.BatchId == movement.GLBatchId && (!movement.CommittedAt.HasValue || x.CreatedAt <= movement.CommittedAt.Value)).ToList();
            if (candidates.Count == 0) return physicalDate;
            return candidates.OrderBy(x => Math.Abs(x.PostingDate.DayNumber - physicalDate.DayNumber)).ThenByDescending(x => x.CreatedAt).First().PostingDate;
        }
        movements = movements.Where(x => BookDate(x) <= asOf).OrderBy(x => x.Date).ThenBy(x => x.Id).ToList();
        var ids = movements.Where(x => !x.ValueChanged.HasValue && (x.Type == StockMovementType.Purchase || x.Type == StockMovementType.Adjustment)).Select(x => x.ItemId).Distinct().ToList();
        var histories = await db.ItemCostHistories.AsNoTracking().Where(x => ids.Contains(x.ItemId)).OrderBy(x => x.DateChanged).ToListAsync();
        var items = await db.Items.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
        var unresolved = new List<StockLedger>();
        var used = new HashSet<Guid>();
        var values = new Dictionary<Guid, decimal>();
        foreach (var movement in movements)
        {
            if (movement.ValueChanged.HasValue) { values[movement.Id] = movement.ValueChanged.Value; continue; }
            var history = histories.FirstOrDefault(x => !used.Contains(x.Id) && x.ItemId == movement.ItemId && x.Reference == movement.Reference && x.NewQtyIn == movement.QuantityChanged);
            if (history != null) used.Add(history.Id);
            else if (movement.Type == StockMovementType.Purchase) { unresolved.Add(movement); continue; }
            var value = history != null ? history.NewQtyIn == 0 ? history.NewCostIn : history.NewQtyIn * history.NewCostIn : movement.QuantityChanged * movement.CostAtTime;
            values[movement.Id] = ReportAccounting.Money(value);
        }
        foreach (var group in unresolved.GroupBy(x => new { x.GLBatchId, x.ItemId, Date = BookDate(x) }))
        {
            var item = items[group.Key.ItemId];
            var batchMovements = movements.Where(x => x.GLBatchId == group.Key.GLBatchId && BookDate(x) == group.Key.Date).ToList();
            var accountItems = batchMovements.Where(x => items.TryGetValue(x.ItemId, out var i) && i.InventoryAssetAccountId == item.InventoryAssetAccountId).Select(x => x.ItemId).Distinct().ToList();
            var entries = ledgerDates.Where(x => x.BatchId == group.Key.GLBatchId && x.PostingDate == group.Key.Date && x.SegCoaId == item.InventoryAssetAccountId).ToList();
            if (!group.Key.GLBatchId.HasValue || accountItems.Count != 1 || entries.Count == 0)
                throw new ArgumentException($"Legacy stock value is unavailable for {item.Code} ({group.First().Reference}). Restore its receipt cost history before reporting this valuation.");
            var remainingValue = entries.Sum(x => x.Debit - x.Credit) - batchMovements.Where(x => x.ItemId == item.Id && values.ContainsKey(x.Id)).Sum(x => values[x.Id]);
            var quantity = group.Sum(x => x.QuantityChanged);
            var rows = group.ToList(); decimal allocated = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var value = i == rows.Count - 1 ? remainingValue - allocated : quantity == 0 ? 0 : Math.Round(remainingValue * rows[i].QuantityChanged / quantity, 4);
                values[rows[i].Id] = value; allocated += value;
            }
        }
        // Older value-only adjustments wrote cost history and GL, but no stock
        // row. Recover only where both the posting and warehouse are verifiable.
        var legacyValues = await db.ItemCostHistories.AsNoTracking().Where(h => h.NewQtyIn == 0 && h.NewCostIn != 0
            && db.Items.Any(i => i.Id == h.ItemId && i.CompanyId == companyId)
            && !db.StockLedgers.IgnoreQueryFilters().Any(m => m.CompanyId == companyId && m.ItemId == h.ItemId && m.Reference == h.Reference && m.QuantityChanged == 0)).ToListAsync();
        if (legacyValues.Count > 0)
        {
            var references = legacyValues.Select(h => h.Reference).Distinct().ToList();
            var batches = await db.GLBatches.AsNoTracking().Where(b => b.CompanyId == companyId && references.Contains(b.Description!)
                && (b.Status == BatchStatus.Posted || b.PostedAt.HasValue)).ToListAsync();
            foreach (var history in legacyValues)
            {
                var item = items[history.ItemId];
                var batchIds = batches.Where(b => b.Description == history.Reference && (!b.PostedAt.HasValue || history.DateChanged <= b.PostedAt)).Select(b => b.Id).ToList();
                var entries = ledgerDates.Where(t => batchIds.Contains(t.BatchId) && t.SegCoaId == item.InventoryAssetAccountId && t.PostingDate <= asOf).ToList();
                if (entries.Count == 0) continue;
                var warehouses = movements.Where(m => m.ItemId == history.ItemId).Select(m => m.WarehouseId).Distinct().ToList();
                var value = entries.Sum(t => t.Debit - t.Credit);
                if (warehouses.Count != 1 || Math.Abs(value - history.NewCostIn) > 0.01m)
                    throw new ArgumentException($"Legacy value adjustment {history.Reference} for {item.Code} needs its warehouse/cost allocation restored before valuation.");
                var adjustment = new StockLedger { ItemId = history.ItemId, WarehouseId = warehouses[0], QuantityChanged = 0, Date = entries.Max(t => t.PostingDate).ToDateTime(TimeOnly.MinValue) };
                movements.Add(adjustment); values[adjustment.Id] = value;
            }
        }
        return movements.GroupBy(x => new { x.ItemId, x.WarehouseId })
            .Where(g => !warehouseId.HasValue || g.Key.WarehouseId == warehouseId.Value)
            .Select(g => new InventoryBookPosition(g.Key.ItemId, g.Key.WarehouseId, g.Sum(x => x.QuantityChanged), g.Sum(x => values[x.Id]), g.Max(x => x.Date)))
            .Where(x => x.Quantity != 0 || x.Value != 0).ToList();
    }
}
