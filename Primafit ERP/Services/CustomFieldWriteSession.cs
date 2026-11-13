using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

/// <summary>A circuit-scoped, explicitly armed document edit. Merely viewing fields never writes them.</summary>
public sealed class CustomFieldWriteSession(IServiceProvider services)
{
    private readonly Dictionary<(CustomFieldScope, Guid), CustomFieldFormState> editors = new();
    private Dictionary<(CustomFieldScope, Guid), CustomFieldFormState>? active;
    private readonly Dictionary<(CustomFieldScope, Guid), Guid> correlations = new();
    public void Register(CustomFieldFormState state)
    {
        var key = (state.Scope, state.OwnerId);
        // Finance shows the selected line in an edit form and a read-only table simultaneously.
        if (editors.TryGetValue(key, out var existing) && existing.CanEdit && !state.CanEdit) return;
        editors[key] = state;
    }
    public void Unregister(CustomFieldFormState state) { var key = (state.Scope, state.OwnerId); if (editors.TryGetValue(key, out var current) && ReferenceEquals(current, state)) editors.Remove(key); }
    public IDisposable BeginRegistered(Guid company, params CustomFieldScope[] scopes) => Begin(editors.Where(p => p.Value.CompanyId == company && p.Value.CanEdit && scopes.Contains(p.Key.Item1)).Select(p => p.Key).ToArray());
    public IDisposable BeginDocument(Guid company, CustomFieldScope header, Guid id, CustomFieldScope line, IEnumerable<Guid> lines)
    {
        var ids = lines.ToHashSet();
        return Begin(editors.Where(p => p.Value.CompanyId == company && p.Value.CanEdit && (p.Key == (header, id) || p.Key.Item1 == line && ids.Contains(p.Key.Item2))).Select(p => p.Key));
    }
    public void Correlate(CustomFieldScope scope, Guid target, Guid source) => correlations[(scope, target)] = source;
    public IDisposable Begin(IEnumerable<(CustomFieldScope Scope, Guid OwnerId)> keys)
    {
        var previous = active; active = new();
        foreach (var key in keys.Distinct())
        {
            if (!editors.TryGetValue(key, out var form)) throw new ArgumentException("Wait for additional fields to finish loading before saving.");
            form.Error = form.Fields.Select(f => f.ParseError).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
            if (form.Error.Length > 0) throw new ArgumentException(form.Error);
            active[key] = form;
        }
        return new Restore(() => { active = previous; correlations.Clear(); });
    }
    public Guid? NewOwnerId(CustomFieldScope scope)
    {
        if (active == null) return null;
        var candidates = active.Where(x => x.Key.Item1 == scope).ToList();
        // A header still supplies document identity when only its line location has configured fields.
        if (candidates.Count == 0 && scope.ToString().EndsWith("Header") && active.Any(x => (int)x.Key.Item1 == (int)scope + 1))
        {
            var company = active.First(x => (int)x.Key.Item1 == (int)scope + 1).Value.CompanyId;
            candidates = editors.Where(x => x.Key.Item1 == scope && x.Value.CompanyId == company).ToList();
        }
        return candidates.Count == 1 && candidates[0].Key.Item2 != Guid.Empty ? candidates[0].Key.Item2 : null;
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
    public async Task ApplyAsync(AppDbContext db)
    {
        if (active == null || active.Count == 0) return;
        var service = services.GetRequiredService<CustomFieldService>();
        var processed = new HashSet<(CustomFieldScope, Guid)>();
        foreach (var pair in active.ToArray())
        {
            var scope = pair.Key.Item1;
            if (!CustomFieldTransactions.Supports(scope)) continue;
            var candidates = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified && CustomFieldTransactions.Bindings.Any(b => b.Scope == scope && b.OwnerType == e.Entity.GetType())).Select(e => e.Entity).ToList();
            if (pair.Key.Item2 != Guid.Empty)
            {
                var existing = await CustomFieldTransactions.FindAsync(db, pair.Value.CompanyId, scope, pair.Key.Item2);
                if (existing != null && candidates.All(x => !ReferenceEquals(x, existing))) candidates.Add(existing);
            }
            foreach (var owner in candidates)
            {
                if (db.Entry(owner).State == EntityState.Deleted) continue;
                var binding = CustomFieldTransactions.Binding(scope, owner); var ownerId = CustomFieldTransactions.Id(owner);
                bool match = ownerId == pair.Key.Item2 || db.Entry(owner).State == EntityState.Added && (pair.Key.Item2 == Guid.Empty || correlations.GetValueOrDefault((scope, ownerId)) == pair.Key.Item2);
                if (!match || !processed.Add((scope, ownerId))) continue;
                if (owner is GLJournalLine manualLine)
                {
                    manualLine.HasManualCustomFields = true;
                    var journal = await db.GLJournalHeaders.FirstOrDefaultAsync(h => h.Id == manualLine.HeaderId);
                    if (journal != null) { journal.HasManualCustomFields = true; var batch = await db.GLBatches.FirstOrDefaultAsync(b => b.Id == journal.BatchId); if (batch != null) batch.CustomFieldContext = (CustomFieldScope)((int)scope - 1); }
                }
                if (owner is GLBatch manualBatch && scope == CustomFieldScope.JournalHeader) manualBatch.CustomFieldContext = scope;
                if (!await CustomFieldTransactions.AcceptAsync(db, binding, owner)) continue;
                var company = await CustomFieldTransactions.CompanyAsync(db, binding, owner);
                if (company != pair.Value.CompanyId) throw new ArgumentException("Custom fields belong to another company.");
                if (owner is GLBatch glBatch && scope == CustomFieldScope.JournalHeader) glBatch.CustomFieldContext = scope;
                var stage = await CustomFieldTransactions.StageAsync(db, binding, owner);
                var stored = db.Entry(owner).State == EntityState.Added ? null : await db.Entry(owner).GetDatabaseValuesAsync();
                if (stored != null)
                {
                    var storedStage = await CustomFieldTransactions.PreviousStageAsync(db, binding, owner);
                    if (storedStage.Locked)
                    {
                        // A list-page posting validates saved values; an armed editor cannot amend a committed record.
                        var previous = await CustomFieldTransactions.ValuesAsync(db, company, scope, ownerId);
                        if (pair.Value.Fields.Any(f => { var v = previous.FirstOrDefault(x => x.DefinitionId == f.Definition.Id); return v == null ? !f.Input.IsEmpty : !CustomFieldService.SameInput(f.Input, CustomFieldInput.From(v)); })) throw new ArgumentException("Committed or in-review custom fields are read-only.");
                        continue;
                    }
                }
                var originalOwnerId = pair.Value.OwnerId; pair.Value.OwnerId = ownerId;
                try { var error = await service.ApplyAsync(db, company, scope, ownerId, owner, pair.Value, stage.Required); if (error.Length > 0) throw new ArgumentException(error); }
                finally { pair.Value.OwnerId = originalOwnerId; }
            }
        }
    }
    public void Saved(AppDbContext db)
    {
        if (active == null) return;
        foreach (var pair in active)
            foreach (var entry in db.ChangeTracker.Entries().Where(e => CustomFieldTransactions.Bindings.Any(b => b.Scope == pair.Key.Item1 && b.OwnerType == e.Entity.GetType())))
                if (CustomFieldTransactions.Id(entry.Entity) == pair.Key.Item2 || correlations.GetValueOrDefault((pair.Key.Item1, CustomFieldTransactions.Id(entry.Entity))) == pair.Key.Item2)
                    pair.Value.ExpectedOwnerVersion = (Guid?)CustomFieldTransactions.Property(entry.Entity, "CustomFieldsVersion");
    }
}
