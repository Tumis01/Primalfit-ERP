using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

internal static class StockValueSnapshots
{
    // Capture the economic value when a movement is saved. CostAtTime in older
    // receipt paths is a resulting WACC, which is not the receipt's value.
    public static void Capture(AppDbContext db)
    {
        var added = db.ChangeTracker.Entries<StockLedger>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        var batchIds = added.Where(x => x.GLBatchId.HasValue).Select(x => x.GLBatchId!.Value).Distinct().ToList();
        var accepted = batchIds.Count == 0 ? new Dictionary<Guid, DateTime?>() : db.GLBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id) && b.Status == BatchStatus.Posted).ToDictionary(b => b.Id, b => b.PostedAt);
        foreach (var movement in added)
            if (movement.GLBatchId.HasValue && accepted.TryGetValue(movement.GLBatchId.Value, out var postedAt)) movement.CommittedAt ??= postedAt ?? DateTime.UtcNow;
        var histories = db.ChangeTracker.Entries<ItemCostHistory>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        var used = new HashSet<Guid>();
        foreach (var entry in db.ChangeTracker.Entries<StockLedger>().Where(x => x.State == EntityState.Added && !x.Entity.ValueChanged.HasValue))
        {
            var movement = entry.Entity;
            var history = histories.FirstOrDefault(h => !used.Contains(h.Id) && h.ItemId == movement.ItemId && h.Reference == movement.Reference && h.NewQtyIn == movement.QuantityChanged);
            if (history != null)
            {
                used.Add(history.Id);
                movement.ValueChanged = Math.Round(history.NewQtyIn == 0 ? history.NewCostIn : history.NewQtyIn * history.NewCostIn, 2, MidpointRounding.AwayFromZero);
            }
            else movement.ValueChanged = Math.Round(movement.QuantityChanged * movement.CostAtTime, 2, MidpointRounding.AwayFromZero);
        }
    }
}
