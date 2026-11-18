using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

/// <summary>Document invariants also apply to non-UI saves and legacy conversions.</summary>
public static class CustomFieldPersistence
{
    public static async Task ValidateAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.DetectChanges();
        foreach (var definition in db.ChangeTracker.Entries<CustomFieldDefinition>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            if (!Enum.IsDefined(definition.DataType)) throw new ArgumentException("Select a supported custom-field type.");
        foreach (var revision in db.ChangeTracker.Entries<CustomFieldRevision>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
        {
            if (string.IsNullOrWhiteSpace(revision.FieldName) || revision.FieldName.Length > 100)
                throw new ArgumentException("Enter a field name of up to 100 characters.");
            if (revision.MaxLength is < 1 or > 4000)
                throw new ArgumentException("Text length must be between 1 and 4,000 characters.");
        }
        if (db.ChangeTracker.Entries<CustomFieldRevision>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Published custom-field revisions are immutable.");
        if (db.ChangeTracker.Entries<CustomFieldDefinition>().Any(e => e.State == EntityState.Modified && new[] { "Code", "DataType", "Scope", "CompanyId" }.Any(p => e.Property(p).IsModified)))
            throw new InvalidOperationException("Published custom-field code, type, scope and company are immutable.");
        var owners = new List<(Guid Company, CustomFieldScope Scope, Guid Id)>();
        var orderEntries = db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            bool editing = entry.State == EntityState.Added || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("CustomFieldsVersion" or "WeightedAverageCost" or "MostRecentCost" or "StandardCost" or "UserSpecifiedCost"));
            if (!editing) continue;
            switch (entry.Entity)
            {
                case Customer c: owners.Add((c.CompanyId, CustomFieldScope.Customer, c.Id)); break;
                case Vendor v: owners.Add((v.CompanyId, CustomFieldScope.Vendor, v.Id)); break;
                case Item i: owners.Add((i.CompanyId, CustomFieldScope.Item, i.Id)); break;
                case SalesOrder o when !o.IsInvoiceDocument && o.Status != OrderStatus.Quote && o.SubmissionState == SalesOrderSubmissionState.Submitted:
                    if (entry.State == EntityState.Added || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is "SubmissionState" or "CustomerId" or "Date" or "CurrencyId" or "WarehouseId" or "ExchangeRate"))
                    { owners.Add((o.CompanyId, CustomFieldScope.SalesOrderHeader, o.Id)); foreach (var l in o.Lines) owners.Add((o.CompanyId, CustomFieldScope.SalesOrderLine, l.Id)); }
                    break;
                case SalesOrderLine l when entry.State == EntityState.Added || entry.Properties.Any(p => p.IsModified && p.Metadata.Name is "ItemId" or "Description" or "Quantity" or "UnitPrice" or "UomId"):
                    var header = orderEntries.FirstOrDefault(e => e.Entity.Id == l.HeaderId)?.Entity ?? await db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == l.HeaderId, cancellationToken);
                    if (header is { SubmissionState: SalesOrderSubmissionState.Submitted } && !header.IsInvoiceDocument && header.Status != OrderStatus.Quote) owners.Add((header.CompanyId, CustomFieldScope.SalesOrderLine, l.Id));
                    break;
            }
        }
        var pending = db.ChangeTracker.Entries().Where(e => e.Entity is CustomFieldValue).ToList();
        foreach (var ownerGroup in owners.Distinct().GroupBy(x => new { x.Company, x.Scope }))
        {
            var defs = await db.Set<CustomFieldDefinition>().Include(d => d.Revisions).Where(d => d.CompanyId == ownerGroup.Key.Company && d.Scope == ownerGroup.Key.Scope && d.IsActive).ToListAsync(cancellationToken);
            var required = defs.Where(d => d.Revisions.Single(r => r.RevisionNumber == d.CurrentRevision).Required).ToList();
            if (required.Count == 0) continue;
            var ids = ownerGroup.Select(o => o.Id).ToList();
            var saved = await SavedAsync(db, ownerGroup.Key.Company, ownerGroup.Key.Scope, ids, cancellationToken);
            foreach (var owner in ownerGroup)
                foreach (var definition in required)
                {
                    var edit = pending.LastOrDefault(e => ((CustomFieldValue)e.Entity).CompanyId == owner.Company && ((CustomFieldValue)e.Entity).OwnerId == owner.Id && ((CustomFieldValue)e.Entity).DefinitionId == definition.Id);
                    var exists = edit != null ? edit.State != EntityState.Deleted : saved.Any(v => v.OwnerId == owner.Id && v.DefinitionId == definition.Id);
                    if (!exists) throw new ArgumentException($"Complete {definition.Revisions.Single(r => r.RevisionNumber == definition.CurrentRevision).FieldName} before saving/submitting this {owner.Scope} record.");
                }
        }
        foreach (var entry in pending.Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var value = (CustomFieldValue)entry.Entity;
            var d = await db.Set<CustomFieldDefinition>().Include(x => x.Revisions).FirstOrDefaultAsync(x => x.Id == value.DefinitionId && x.CompanyId == value.CompanyId, cancellationToken) ?? throw new ArgumentException("Invalid custom-field company/definition.");
            var revision = d.Revisions.SingleOrDefault(r => r.Id == value.RevisionId)
                ?? throw new ArgumentException("Invalid custom-field revision.");
            if (value.TextValue != null && (string.IsNullOrWhiteSpace(value.TextValue) || value.TextValue.Length > revision.MaxLength))
                throw new ArgumentException($"{revision.FieldName} allows at most {revision.MaxLength} characters and cannot be blank.");
            var scope = value switch { CustomerCustomFieldValue => CustomFieldScope.Customer, VendorCustomFieldValue => CustomFieldScope.Vendor, ItemCustomFieldValue => CustomFieldScope.Item, SalesOrderCustomFieldValue => CustomFieldScope.SalesOrderHeader, _ => CustomFieldScope.SalesOrderLine };
            bool validType = d.DataType switch { CustomFieldDataType.Text => value.TextValue != null, CustomFieldDataType.Decimal => value.DecimalValue.HasValue, CustomFieldDataType.Date => value.DateValue.HasValue, CustomFieldDataType.Boolean => value.BooleanValue.HasValue, _ => value.OptionId.HasValue };
            var generic = value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(TransactionCustomFieldValue<>);
            var scopeMatches = generic ? Primafit_ERP.Services.CustomFieldTransactions.Bindings.Any(b => b.Scope == d.Scope && b.OwnerType == value.GetType().GetGenericArguments()[0]) : d.Scope == scope;
            if (!scopeMatches || !validType) throw new ArgumentException("Invalid custom-field scope or typed value.");
        }
    }
    private static async Task<List<CustomFieldValue>> SavedAsync(AppDbContext db, Guid company, CustomFieldScope scope, List<Guid> ids, CancellationToken ct) => scope switch {
        CustomFieldScope.Customer => (await db.Set<CustomerCustomFieldValue>().AsNoTracking().Where(v => v.CompanyId == company && ids.Contains(v.CustomerId)).ToListAsync(ct)).Cast<CustomFieldValue>().ToList(),
        CustomFieldScope.Vendor => (await db.Set<VendorCustomFieldValue>().AsNoTracking().Where(v => v.CompanyId == company && ids.Contains(v.VendorId)).ToListAsync(ct)).Cast<CustomFieldValue>().ToList(),
        CustomFieldScope.Item => (await db.Set<ItemCustomFieldValue>().AsNoTracking().Where(v => v.CompanyId == company && ids.Contains(v.ItemId)).ToListAsync(ct)).Cast<CustomFieldValue>().ToList(),
        CustomFieldScope.SalesOrderHeader => (await db.Set<SalesOrderCustomFieldValue>().AsNoTracking().Where(v => v.CompanyId == company && ids.Contains(v.SalesOrderId)).ToListAsync(ct)).Cast<CustomFieldValue>().ToList(),
        _ => (await db.Set<SalesOrderLineCustomFieldValue>().AsNoTracking().Where(v => v.CompanyId == company && ids.Contains(v.SalesOrderLineId)).ToListAsync(ct)).Cast<CustomFieldValue>().ToList()
    };
}
