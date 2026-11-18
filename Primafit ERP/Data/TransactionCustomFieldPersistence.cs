using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;

namespace PrimafitERP.Data;

public static class TransactionCustomFieldPersistence
{
    private static async Task PromoteReceiptsAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var receipt in db.ChangeTracker.Entries<GoodsReceipt>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList())
        {
            var header = await db.Set<TransactionCustomFieldValue<GoodsReceiptDraft>>().AsNoTracking().Where(v => v.CompanyId == receipt.CompanyId && v.RecordId == receipt.Id).ToListAsync(ct);
            foreach (var value in header) CopyValue(db, value, receipt);
            foreach (var line in receipt.Lines)
            {
                var values = await db.Set<TransactionCustomFieldValue<GoodsReceiptDraftLine>>().AsNoTracking().Where(v => v.CompanyId == receipt.CompanyId && v.ParentId == receipt.Id && v.RecordId == line.Id).ToListAsync(ct);
                foreach (var value in values) CopyValue(db, value, line);
            }
        }
    }
    private static void CopyValue(AppDbContext db, CustomFieldValue source, object owner)
    {
        var scope = owner is GoodsReceipt ? CustomFieldScope.GoodsReceiptHeader : CustomFieldScope.GoodsReceiptLine;
        var target = CustomFieldTransactions.NewValue(scope, owner); var id = CustomFieldTransactions.Id(owner);
        if (db.ChangeTracker.Entries().Any(e => e.Entity.GetType() == target.GetType() && ((CustomFieldValue)e.Entity).OwnerId == id && ((CustomFieldValue)e.Entity).DefinitionId == source.DefinitionId)) return;
        // Types differ, so copy only scalar value/audit metadata and assign the real destination keys.
        foreach (var p in typeof(CustomFieldValue).GetProperties().Where(p => p.CanWrite && p.Name is not ("Id" or "OwnerId"))) p.SetValue(target, p.GetValue(source));
        target.Id = Guid.NewGuid(); target.OwnerId = id; db.Add(target);
    }
    public static async Task PrepareAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (db.CustomFieldWrites != null) await db.CustomFieldWrites.ApplyAsync(db);
        await PromoteReceiptsAsync(db, ct);
        db.ChangeTracker.DetectChanges();
        foreach (var edit in db.ChangeTracker.Entries().Where(e => e.Entity is CustomFieldValue && e.Entity.GetType().IsGenericType && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            var value = (CustomFieldValue)edit.Entity;
            var definition = await db.Set<CustomFieldDefinition>().Include(d => d.Revisions).FirstOrDefaultAsync(d => d.Id == value.DefinitionId && d.CompanyId == value.CompanyId, ct) ?? throw new ArgumentException("Invalid custom-field definition.");
            var binding = CustomFieldTransactions.Bindings.SingleOrDefault(b => b.Scope == definition.Scope && b.OwnerType == edit.Entity.GetType().GetGenericArguments()[0]) ?? throw new ArgumentException("Invalid custom-field scope.");
            var owner = await db.FindAsync(binding.OwnerType, value.OwnerId) ?? throw new ArgumentException("Custom-field owner was not found.");
            if (db.Entry(owner).State == EntityState.Deleted) continue;
            if (!await CustomFieldTransactions.AcceptAsync(db, binding, owner)) throw new ArgumentException("Custom-field scope does not match this transaction.");
            var stored = db.Entry(owner).State == EntityState.Added ? null : await db.Entry(owner).GetDatabaseValuesAsync(ct);
            if (stored != null && (await CustomFieldTransactions.PreviousStageAsync(db, binding, owner)).Locked) throw new ArgumentException("Committed or in-review custom fields are read-only.");
            if (edit.State == EntityState.Deleted)
            {
                var required = definition.Revisions.Single(r => r.RevisionNumber == definition.CurrentRevision);
                if (definition.IsActive && required.Required && (await CustomFieldTransactions.StageAsync(db, binding, owner)).Required) throw new ArgumentException($"Complete {required.FieldName} before removing this required transaction value.");
                continue;
            }
            var revision = definition.Revisions.SingleOrDefault(r => r.Id == value.RevisionId) ?? throw new ArgumentException("Invalid custom-field revision.");
            if (value.TextValue != null && (string.IsNullOrWhiteSpace(value.TextValue) || value.TextValue.Length > revision.MaxLength)) throw new ArgumentException("Invalid custom-field text length.");
            if (value.DecimalValue is decimal amount && (amount < (revision.Minimum ?? decimal.MinValue) || amount > (revision.Maximum ?? decimal.MaxValue) || Math.Round(amount, revision.DecimalPlaces) != amount)) throw new ArgumentException("Invalid custom-field decimal range or precision.");
        }
        foreach (var batch in db.ChangeTracker.Entries<GLBatch>().Where(e => e.State == EntityState.Modified && e.Entity.Status == BatchStatus.Posted && e.Property(b => b.Status).IsModified).Select(e => e.Entity).ToList())
            {
            foreach (var adjustment in await db.Set<InventoryAdjustmentDocument>().Where(a => a.CompanyId == batch.CompanyId && a.GLBatchId == batch.Id && a.CommittedAt == null).ToListAsync(ct)) adjustment.CommittedAt = DateTime.UtcNow;
            foreach (var stockEntry in await db.Set<InventoryEntryDocument>().Where(a => a.CompanyId == batch.CompanyId && a.GLBatchId == batch.Id && a.CommittedAt == null).ToListAsync(ct)) stockEntry.CommittedAt = DateTime.UtcNow;
            }
        var entries = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
        var ownersToCheck = entries.Select(e => e.Entity).ToList();
        foreach (var root in ownersToCheck.ToList())
            foreach (var binding in CustomFieldTransactions.Bindings.Where(b => b.OwnerType == root.GetType()))
            {
                if (!await CustomFieldTransactions.AcceptAsync(db, binding, root) || !(await CustomFieldTransactions.StageAsync(db, binding, root)).Required) continue;
                foreach (var childBinding in CustomFieldTransactions.Bindings.Where(b => b.ParentType == root.GetType())) ownersToCheck.AddRange(await CustomFieldTransactions.ChildrenAsync(db, childBinding, CustomFieldTransactions.Id(root)));
                if (root is GLBatch gl)
                    ownersToCheck.AddRange(await db.GLJournalLines.Where(l => db.GLJournalHeaders.Any(h => h.Id == l.HeaderId && h.BatchId == gl.Id)).ToListAsync(ct));
            }
        foreach (var owner in ownersToCheck.Distinct())
        {
            var entry = db.Entry(owner); if (entry.State == EntityState.Deleted) continue;
            foreach (var binding in CustomFieldTransactions.Bindings.Where(b => b.OwnerType == entry.Entity.GetType()))
            {
                if (!await CustomFieldTransactions.AcceptAsync(db, binding, entry.Entity)) continue;
                var company = await CustomFieldTransactions.CompanyAsync(db, binding, entry.Entity); if (company == Guid.Empty) continue;
                var stage = await CustomFieldTransactions.StageAsync(db, binding, entry.Entity);
                if (!stage.Required) continue;
                var original = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync(ct);
                if (original != null && await CustomFieldTransactions.CommittedAsync(db, binding, original.ToObject())) continue;
                var definitions = await db.Set<CustomFieldDefinition>().Include(d => d.Revisions).Where(d => d.CompanyId == company && d.Scope == binding.Scope && d.IsActive).ToListAsync(ct);
                var required = definitions.Where(d => d.Revisions.Single(r => r.RevisionNumber == d.CurrentRevision).Required).ToList(); if (required.Count == 0) continue;
                var id = CustomFieldTransactions.Id(entry.Entity); var saved = await CustomFieldTransactions.ValuesAsync(db, company, binding.Scope, id);
                foreach (var d in required)
                {
                    var valueType = typeof(TransactionCustomFieldValue<>).MakeGenericType(binding.OwnerType);
                    var pending = db.ChangeTracker.Entries().LastOrDefault(e => e.Entity.GetType() == valueType && ((CustomFieldValue)e.Entity).OwnerId == id && ((CustomFieldValue)e.Entity).DefinitionId == d.Id);
                    var found = pending == null ? saved.Any(v => v.DefinitionId == d.Id) : pending.State != EntityState.Deleted;
                    if (!found) throw new ArgumentException($"Complete {d.Revisions.Single(r => r.RevisionNumber == d.CurrentRevision).FieldName} for {CustomFieldService.ScopeLabel(binding.Scope)}.");
                }
            }
        }
    }
}
