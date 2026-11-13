using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

/// <summary>Document invariants also apply to non-UI saves and legacy conversions.</summary>
public static class CustomFieldPersistence
{
    public static async Task ValidateAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.DetectChanges();
        if (db.ChangeTracker.Entries<CustomFieldRevision>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Published custom-field revisions are immutable.");
        if (db.ChangeTracker.Entries<CustomFieldDefinition>().Any(e => e.State == EntityState.Modified && new[] { "Code", "DataType", "Scope", "CompanyId" }.Any(p => e.Property(p).IsModified)))
            throw new InvalidOperationException("Published custom-field code, type, scope and company are immutable.");
        var extendedNames = new Dictionary<string, CustomFieldScope[]> {
            ["GLBatch"] = new[] { CustomFieldScope.JournalHeader }, ["GLJournalHeader"] = new[] { CustomFieldScope.JournalHeader, CustomFieldScope.JournalLine },
            ["CashbookBatch"] = new[] { CustomFieldScope.CashbookHeader, CustomFieldScope.CashbookLine },
            ["PurchaseOrder"] = new[] { CustomFieldScope.PurchaseOrderHeader, CustomFieldScope.PurchaseOrderLine },
            ["PurchaseInvoice"] = new[] { CustomFieldScope.PurchaseInvoiceHeader, CustomFieldScope.PurchaseInvoiceLine },
            ["GoodsReceipt"] = new[] { CustomFieldScope.GoodsReceiptHeader, CustomFieldScope.GoodsReceiptLine },
            ["SalesShipment"] = new[] { CustomFieldScope.ShipmentHeader, CustomFieldScope.ShipmentLine },
            ["StockTransfer"] = new[] { CustomFieldScope.StockTransferHeader, CustomFieldScope.StockTransferLine },
            ["SalesInvoice"] = new[] { CustomFieldScope.SalesInvoiceHeader, CustomFieldScope.SalesInvoiceLine },
            ["CreditNote"] = new[] { CustomFieldScope.CreditNoteHeader, CustomFieldScope.CreditNoteLine },
            ["DebitNote"] = new[] { CustomFieldScope.DebitNoteHeader, CustomFieldScope.DebitNoteLine },
            ["ReceiptRefund"] = new[] { CustomFieldScope.ReceiptRefundHeader, CustomFieldScope.ReceiptRefundLine },
            ["VendorReturn"] = new[] { CustomFieldScope.VendorReturnHeader, CustomFieldScope.VendorReturnLine },
            ["CustomerPayment"] = new[] { CustomFieldScope.CustomerPayment }
        };
        extendedNames["GLBatch"] = new[] { CustomFieldScope.JournalHeader, CustomFieldScope.JournalLine, CustomFieldScope.GeneralLedgerAdjustmentHeader, CustomFieldScope.GeneralLedgerAdjustmentLine, CustomFieldScope.CustomerAdjustmentHeader, CustomFieldScope.CustomerAdjustmentLine, CustomFieldScope.VendorAdjustmentHeader, CustomFieldScope.VendorAdjustmentLine };
        extendedNames["GLJournalHeader"] = extendedNames["GLBatch"];
        extendedNames["StockLedger"] = new[] { CustomFieldScope.StockEntryHeader, CustomFieldScope.StockEntryLine, CustomFieldScope.InventoryAdjustmentHeader, CustomFieldScope.InventoryAdjustmentLine };
        extendedNames["BankReconciliation"] = new[] { CustomFieldScope.BankReconciliationHeader, CustomFieldScope.BankReconciliationLine };
        extendedNames["VendorPayment"] = new[] { CustomFieldScope.VendorPayment };
        extendedNames["PurchaseOrder"] = new[] { CustomFieldScope.PurchaseRequestHeader, CustomFieldScope.PurchaseRequestLine, CustomFieldScope.PurchaseOrderHeader, CustomFieldScope.PurchaseOrderLine, CustomFieldScope.PurchaseInvoiceHeader, CustomFieldScope.PurchaseInvoiceLine };
        extendedNames["SalesOrder"] = new[] { CustomFieldScope.SalesQuoteHeader, CustomFieldScope.SalesQuoteLine, CustomFieldScope.SalesInvoiceHeader, CustomFieldScope.SalesInvoiceLine };
        async Task<(Guid Company, CustomFieldScope[] Scopes)> Resolve(object entity, int depth = 0)
        {
            if (depth > 4) return (Guid.Empty, Array.Empty<CustomFieldScope>());
            var type = entity.GetType();
            if (extendedNames.TryGetValue(type.Name, out var scopes) && type.GetProperty("CompanyId")?.GetValue(entity) is Guid direct) return (direct, scopes);
            foreach (var key in db.Entry(entity).Metadata.GetForeignKeys())
            {
                if (!extendedNames.ContainsKey(key.PrincipalEntityType.ClrType.Name) && key.PrincipalEntityType.ClrType.Name is not "GLJournalHeader") continue;
                var values = key.Properties.Select(p => db.Entry(entity).Property(p.Name).CurrentValue).ToArray();
                if (values.Any(v => v == null)) continue;
                var parent = await db.FindAsync(key.PrincipalEntityType.ClrType, values, cancellationToken);
                if (parent == null) continue;
                var result = await Resolve(parent, depth + 1);
                if (result.Company != Guid.Empty) return result;
            }
            return (Guid.Empty, Array.Empty<CustomFieldScope>());
        }
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            if (entry.Entity is CustomFieldDefinition or CustomFieldRevision or CustomFieldOption or CustomFieldValue) continue;
            var resolved = await Resolve(entry.Entity);
            if (resolved.Company != Guid.Empty && await db.Set<CustomFieldDefinition>().AnyAsync(d => d.CompanyId == resolved.Company && d.IsActive && resolved.Scopes.Contains(d.Scope), cancellationToken))
                throw new ArgumentException("Use the web transaction editor for this custom-field-enabled document; this legacy API does not accept its field values.");
        }
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
                    if (!exists) throw new ArgumentException($"Complete {definition.Revisions.Single(r => r.RevisionNumber == definition.CurrentRevision).Label} before saving/submitting this {owner.Scope} record.");
                }
        }
        foreach (var entry in pending.Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var value = (CustomFieldValue)entry.Entity;
            var d = await db.Set<CustomFieldDefinition>().FirstOrDefaultAsync(x => x.Id == value.DefinitionId && x.CompanyId == value.CompanyId, cancellationToken) ?? throw new ArgumentException("Invalid custom-field company/definition.");
            var scope = value switch { CustomerCustomFieldValue => CustomFieldScope.Customer, VendorCustomFieldValue => CustomFieldScope.Vendor, ItemCustomFieldValue => CustomFieldScope.Item, SalesOrderCustomFieldValue => CustomFieldScope.SalesOrderHeader, _ => CustomFieldScope.SalesOrderLine };
            bool validType = d.DataType switch { CustomFieldDataType.ShortText or CustomFieldDataType.LongText => value.TextValue != null, CustomFieldDataType.Decimal => value.DecimalValue.HasValue, CustomFieldDataType.Date => value.DateValue.HasValue, CustomFieldDataType.Boolean => value.BooleanValue.HasValue, _ => value.OptionId.HasValue };
            if (d.Scope != scope || !validType) throw new ArgumentException("Invalid custom-field scope or typed value.");
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
