using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public static class CustomFieldLineIdentity
{
    public static void Sync<T>(AppDbContext db, ICollection<T> existing, IEnumerable<T> incoming, string parentProperty, Guid parentId) where T : class, new()
    {
        var type = typeof(T); var key = type.GetProperty("Id")!; var parent = type.GetProperty(parentProperty)!;
        var rows = incoming.ToList();
        foreach (var row in rows) if ((Guid)key.GetValue(row)! == Guid.Empty) key.SetValue(row, Guid.NewGuid());
        if (rows.GroupBy(r => (Guid)key.GetValue(r)!).Any(g => g.Count() > 1)) throw new ArgumentException("A document line cannot be repeated.");
        var wanted = rows.Select(r => (Guid)key.GetValue(r)!).ToHashSet();
        foreach (var removed in existing.Where(r => !wanted.Contains((Guid)key.GetValue(r)!)).ToList()) { db.Remove(removed); existing.Remove(removed); }
        foreach (var source in rows)
        {
            // The parent participates in the alternate key: never assign an empty DTO parent to a tracked row.
            parent.SetValue(source, parentId);
            var id = (Guid)key.GetValue(source)!; var target = existing.FirstOrDefault(r => (Guid)key.GetValue(r)! == id);
            if (target == null) { target = new T(); db.Entry(target).CurrentValues.SetValues(source); parent.SetValue(target, parentId); existing.Add(target); db.Add(target); }
            else
            {
                var version = type.GetProperty("CustomFieldsVersion"); var savedVersion = version?.GetValue(target);
                db.Entry(target).CurrentValues.SetValues(source); parent.SetValue(target, parentId); version?.SetValue(target, savedVersion);
            }
        }
    }
}
