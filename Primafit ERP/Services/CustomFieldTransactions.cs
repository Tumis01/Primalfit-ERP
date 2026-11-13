using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed record CustomFieldBinding(CustomFieldScope Scope, Type OwnerType, string Module, string EditPermission, string ViewPermission,
    string ReferenceProperty, string? DateProperty, Type? ParentType = null, string? ParentProperty = null, string? ParentReference = null, string? ParentDate = null);

/// <summary>Only this allowlisted registry can expose a transaction owner to custom fields.</summary>
public static class CustomFieldTransactions
{
    /// <summary>Validate before inventory calls the separately committed GL staging engine.</summary>
    public static async Task ValidateNewAsync(IDbContextFactory<AppDbContext> factory, object owner, IEnumerable<object> lines)
    {
        await using var preview = await factory.CreateDbContextAsync();
        // Copy scalars only: referenced items, accounts and warehouses are existing master records.
        foreach (var source in new[] { owner }.Concat(lines))
        {
            var copy = Activator.CreateInstance(source.GetType())!;
            preview.Entry(copy).CurrentValues.SetValues(source);
            preview.Entry(copy).State = EntityState.Added;
        }
        await TransactionCustomFieldPersistence.PrepareAsync(preview);
    }
    public static readonly IReadOnlyList<CustomFieldBinding> Bindings = new List<CustomFieldBinding>
    {
        new(CustomFieldScope.StockEntryHeader, typeof(InventoryEntryDocument), "Inventory", "INVENTORY_OPERATIONS_MANAGE", "INVENTORY_OPERATIONS_PAGE", "Reference", "Date"),
        new(CustomFieldScope.StockEntryLine, typeof(InventoryEntryRecordLine), "Inventory", "INVENTORY_OPERATIONS_MANAGE", "INVENTORY_OPERATIONS_PAGE", "ItemId", null, typeof(InventoryEntryDocument), "HeaderId", "Reference", "Date"),
        new(CustomFieldScope.BankReconciliationHeader, typeof(BankReconciliation), "Finance", "RECON_START", "RECON_PAGE", "Id", "StatementDate"),
        new(CustomFieldScope.BankReconciliationLine, typeof(BankStatementLine), "Finance", "RECON_START", "RECON_PAGE", "Reference", "Date", typeof(BankReconciliation), "ReconciliationId", "Id", "StatementDate"),
        new(CustomFieldScope.GeneralLedgerAdjustmentHeader, typeof(GLBatch), "Finance", "GL_JOURNAL_CREATE", "GL_PAGE", "BatchName", "CreatedAt"),
        new(CustomFieldScope.GeneralLedgerAdjustmentLine, typeof(GLJournalLine), "Finance", "GL_JOURNAL_CREATE", "GL_PAGE", "Reference", null, typeof(GLJournalHeader), "HeaderId", "JournalNumber", "TransactionDate"),
        new(CustomFieldScope.CustomerAdjustmentHeader, typeof(GLBatch), "Sales", "SALES_ORDER_CREATE", "SALES_ORDER_PAGE", "BatchName", "CreatedAt"),
        new(CustomFieldScope.CustomerAdjustmentLine, typeof(GLJournalLine), "Sales", "SALES_ORDER_CREATE", "SALES_ORDER_PAGE", "Reference", null, typeof(GLJournalHeader), "HeaderId", "JournalNumber", "TransactionDate"),
        new(CustomFieldScope.VendorAdjustmentHeader, typeof(GLBatch), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "BatchName", "CreatedAt"),
        new(CustomFieldScope.VendorAdjustmentLine, typeof(GLJournalLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "Reference", null, typeof(GLJournalHeader), "HeaderId", "JournalNumber", "TransactionDate"),
        new(CustomFieldScope.JournalHeader, typeof(GLBatch), "Finance", "GL_JOURNAL_CREATE", "GL_PAGE", "BatchName", "CreatedAt"),
        new(CustomFieldScope.JournalLine, typeof(GLJournalLine), "Finance", "GL_JOURNAL_CREATE", "GL_PAGE", "Reference", null, typeof(GLJournalHeader), "HeaderId", "JournalNumber", "TransactionDate"),
        new(CustomFieldScope.CashbookHeader, typeof(CashbookBatch), "Finance", "CASHBOOK_BATCH_CREATE", "CASHBOOK_PAGE", "BatchReference", "CreatedDate"),
        new(CustomFieldScope.CashbookLine, typeof(CashbookEntry), "Finance", "CASHBOOK_BATCH_CREATE", "CASHBOOK_PAGE", "Reference", "TransactionDate", typeof(CashbookBatch), "CashbookBatchId", "BatchReference", "CreatedDate"),
        new(CustomFieldScope.SalesQuoteHeader, typeof(SalesQuote), "Sales", "SALES_ORDER_CREATE", "SALES_ORDER_PAGE", "QuoteNumber", "Date"),
        new(CustomFieldScope.SalesQuoteLine, typeof(SalesQuoteLine), "Sales", "SALES_ORDER_CREATE", "SALES_ORDER_PAGE", "Description", null, typeof(SalesQuote), "SalesQuoteId", "QuoteNumber", "Date"),
        new(CustomFieldScope.SalesInvoiceHeader, typeof(SalesInvoice), "Sales", "SALES_INVOICE_CREATE", "SALES_ORDER_PAGE", "InvoiceNumber", "Date"),
        new(CustomFieldScope.SalesInvoiceLine, typeof(SalesInvoiceLine), "Sales", "SALES_INVOICE_CREATE", "SALES_ORDER_PAGE", "Description", null, typeof(SalesInvoice), "SalesInvoiceId", "InvoiceNumber", "Date"),
        new(CustomFieldScope.PurchaseRequestHeader, typeof(PurchaseRequest), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "RequestNumber", "RequestDate"),
        new(CustomFieldScope.PurchaseRequestLine, typeof(PurchaseRequestLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "Description", null, typeof(PurchaseRequest), "PurchaseRequestId", "RequestNumber", "RequestDate"),
        new(CustomFieldScope.PurchaseOrderHeader, typeof(PurchaseOrder), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "OrderNumber", "OrderDate"),
        new(CustomFieldScope.PurchaseOrderLine, typeof(PurchaseOrderLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "Description", null, typeof(PurchaseOrder), "PurchaseOrderId", "OrderNumber", "OrderDate"),
        new(CustomFieldScope.PurchaseInvoiceHeader, typeof(PurchaseInvoice), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "PurchaseInvoiceNumber", "InvoiceDate"),
        new(CustomFieldScope.PurchaseInvoiceLine, typeof(PurchaseInvoiceLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "Description", null, typeof(PurchaseInvoice), "PurchaseInvoiceId", "PurchaseInvoiceNumber", "InvoiceDate"),
        new(CustomFieldScope.GoodsReceiptHeader, typeof(GoodsReceipt), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "GrnNumber", "DateReceived"),
        new(CustomFieldScope.GoodsReceiptLine, typeof(GoodsReceiptLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "UomName", null, typeof(GoodsReceipt), "GoodsReceiptId", "GrnNumber", "DateReceived"),
        new(CustomFieldScope.GoodsReceiptHeader, typeof(GoodsReceiptDraft), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "GrnNumber", "DateReceived"),
        new(CustomFieldScope.GoodsReceiptLine, typeof(GoodsReceiptDraftLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "UomName", null, typeof(GoodsReceiptDraft), "GoodsReceiptDraftId", "GrnNumber", "DateReceived"),
        new(CustomFieldScope.ShipmentHeader, typeof(SalesShipment), "Inventory", "SHIPMENT_CREATE", "WH_SHIPMENTS_PAGE", "ShipmentNumber", "CreatedDate"),
        new(CustomFieldScope.ShipmentLine, typeof(SalesShipmentLine), "Inventory", "SHIPMENT_CREATE", "WH_SHIPMENTS_PAGE", "UomName", null, typeof(SalesShipment), "ShipmentId", "ShipmentNumber", "CreatedDate"),
        new(CustomFieldScope.StockTransferHeader, typeof(StockTransfer), "Inventory", "INVENTORY_OPERATIONS_MANAGE", "INVENTORY_OPERATIONS_PAGE", "Reference", "DateShipped"),
        new(CustomFieldScope.StockTransferLine, typeof(StockTransferLine), "Inventory", "INVENTORY_OPERATIONS_MANAGE", "INVENTORY_OPERATIONS_PAGE", "UomName", null, typeof(StockTransfer), "StockTransferId", "Reference", "DateShipped"),
        new(CustomFieldScope.CreditNoteHeader, typeof(CreditNote), "Sales", "CREDIT_NOTE_CREATE", "CREDIT_NOTE_PAGE", "CreditNoteNumber", "Date"),
        new(CustomFieldScope.CreditNoteLine, typeof(CreditNoteLine), "Sales", "CREDIT_NOTE_CREATE", "CREDIT_NOTE_PAGE", "UomName", null, typeof(CreditNote), "HeaderId", "CreditNoteNumber", "Date"),
        new(CustomFieldScope.DebitNoteHeader, typeof(DebitNote), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "DebitNoteNumber", "Date"),
        new(CustomFieldScope.DebitNoteLine, typeof(DebitNoteLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "UomName", null, typeof(DebitNote), "HeaderId", "DebitNoteNumber", "Date"),
        new(CustomFieldScope.ReceiptRefundHeader, typeof(ReceiptRefund), "Sales", "AR_PAYMENT_CREATE", "AR_PAYMENT_PAGE", "RefundNumber", "Date"),
        new(CustomFieldScope.ReceiptRefundLine, typeof(ReceiptRefundLine), "Sales", "AR_PAYMENT_CREATE", "AR_PAYMENT_PAGE", "UomName", null, typeof(ReceiptRefund), "HeaderId", "RefundNumber", "Date"),
        new(CustomFieldScope.VendorReturnHeader, typeof(VendorReturn), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "ReturnNumber", "Date"),
        new(CustomFieldScope.VendorReturnLine, typeof(VendorReturnLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "UomName", null, typeof(VendorReturn), "HeaderId", "ReturnNumber", "Date"),
        new(CustomFieldScope.CustomerPayment, typeof(CustomerPayment), "Sales", "AR_PAYMENT_CREATE", "AR_PAYMENT_PAGE", "Reference", "Date"),
        new(CustomFieldScope.VendorPayment, typeof(VendorPayment), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "Reference", "Date", typeof(PurchaseInvoice), "PurchaseInvoiceId", "PurchaseInvoiceNumber", "InvoiceDate"),
        new(CustomFieldScope.InventoryAdjustmentHeader, typeof(InventoryAdjustmentDocument), "Inventory", "INVENTORY_OPERATIONS_MANAGE", "INVENTORY_OPERATIONS_PAGE", "Reference", "Date"),
        new(CustomFieldScope.InventoryAdjustmentLine, typeof(InventoryAdjustmentRecordLine), "Inventory", "INVENTORY_OPERATIONS_MANAGE", "INVENTORY_OPERATIONS_PAGE", "ItemId", null, typeof(InventoryAdjustmentDocument), "HeaderId", "Reference", "Date"),
        new(CustomFieldScope.SalesQuoteHeader, typeof(SalesOrder), "Sales", "SALES_ORDER_CREATE", "SALES_ORDER_PAGE", "OrderNumber", "Date"),
        new(CustomFieldScope.SalesQuoteLine, typeof(SalesOrderLine), "Sales", "SALES_ORDER_CREATE", "SALES_ORDER_PAGE", "Description", null, typeof(SalesOrder), "HeaderId", "OrderNumber", "Date"),
        new(CustomFieldScope.SalesInvoiceHeader, typeof(SalesOrder), "Sales", "SALES_INVOICE_CREATE", "SALES_ORDER_PAGE", "OrderNumber", "Date"),
        new(CustomFieldScope.SalesInvoiceLine, typeof(SalesOrderLine), "Sales", "SALES_INVOICE_CREATE", "SALES_ORDER_PAGE", "Description", null, typeof(SalesOrder), "HeaderId", "OrderNumber", "Date"),
        new(CustomFieldScope.PurchaseRequestHeader, typeof(PurchaseOrder), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "OrderNumber", "OrderDate"),
        new(CustomFieldScope.PurchaseRequestLine, typeof(PurchaseOrderLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "Description", null, typeof(PurchaseOrder), "PurchaseOrderId", "OrderNumber", "OrderDate"),
        new(CustomFieldScope.PurchaseInvoiceHeader, typeof(PurchaseOrder), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "OrderNumber", "OrderDate"),
        new(CustomFieldScope.PurchaseInvoiceLine, typeof(PurchaseOrderLine), "Purchasing", "PO_CREATE", "PURCHASING_PAGE", "Description", null, typeof(PurchaseOrder), "PurchaseOrderId", "OrderNumber", "OrderDate")
    };
    public static bool Supports(CustomFieldScope scope) => Bindings.Any(b => b.Scope == scope);
    public static CustomFieldBinding Binding(CustomFieldScope scope) => Bindings.First(b => b.Scope == scope);
    public static CustomFieldBinding Binding(CustomFieldScope scope, object owner) => Bindings.Single(b => b.Scope == scope && b.OwnerType.IsInstanceOfType(owner));
    public static Guid Id(object o) => (Guid)o.GetType().GetProperty("Id")!.GetValue(o)!;
    public static object? Property(object o, string? name) => name == null ? null : o.GetType().GetProperty(name)?.GetValue(o);
    public static async Task<object?> ParentAsync(AppDbContext db, CustomFieldBinding b, object owner) => b.ParentType == null ? null : await db.FindAsync(b.ParentType, Property(owner, b.ParentProperty));
    public static async Task<Guid> CompanyAsync(AppDbContext db, CustomFieldBinding b, object owner)
    {
        var direct = Property(owner, "CompanyId"); if (direct is Guid company) return company;
        var parent = await ParentAsync(db, b, owner); return parent == null ? Guid.Empty : (Guid)(Property(parent, "CompanyId") ?? Guid.Empty);
    }
    public static async Task<object?> FindAsync(AppDbContext db, Guid company, CustomFieldScope scope, Guid id)
    {
        foreach (var b in Bindings.Where(x => x.Scope == scope)) { var owner = await db.FindAsync(b.OwnerType, id); if (owner != null && await CompanyAsync(db, b, owner) == company && await AcceptAsync(db, b, owner)) return owner; }
        return null;
    }
    public static async Task<bool> AcceptAsync(AppDbContext db, CustomFieldBinding binding, object owner)
    {
        var root = await ParentAsync(db, binding, owner) ?? owner;
        if (root is GLBatch gl) return gl.CustomFieldContext == binding.Scope || binding.Scope == CustomFieldScope.JournalHeader && gl.CustomFieldContext == null && gl.BatchName.StartsWith("JV-", StringComparison.OrdinalIgnoreCase);
        if (root is GLJournalHeader gh)
        {
            var batch = await db.GLBatches.FindAsync(gh.BatchId);
            if (binding.Scope == CustomFieldScope.JournalLine) return batch != null && (batch.CustomFieldContext == CustomFieldScope.JournalHeader || batch.CustomFieldContext == null && batch.BatchName.StartsWith("JV-", StringComparison.OrdinalIgnoreCase));
            return gh.HasManualCustomFields && batch?.CustomFieldContext == (CustomFieldScope)((int)binding.Scope - 1) && owner is GLJournalLine { HasManualCustomFields: true };
        }
        if (binding.Scope == CustomFieldScope.JournalLine && root is GLJournalHeader journal && !journal.HasManualCustomFields) return false;
        if (root is SalesOrder sales) return binding.Scope is CustomFieldScope.SalesQuoteHeader or CustomFieldScope.SalesQuoteLine ? sales.Status == OrderStatus.Quote : sales.IsInvoiceDocument;
        if (root is PurchaseOrder purchase) return binding.Scope switch {
            CustomFieldScope.PurchaseRequestHeader or CustomFieldScope.PurchaseRequestLine => purchase.Status == PurchaseOrderStatus.Request,
            CustomFieldScope.PurchaseInvoiceHeader or CustomFieldScope.PurchaseInvoiceLine => purchase.IsInvoiceDocument,
            _ => !purchase.IsInvoiceDocument && purchase.Status != PurchaseOrderStatus.Request
        };
        return true;
    }
    public static CustomFieldValue NewValue(CustomFieldScope scope, object owner)
    {
        var b = Binding(scope, owner); var value = (CustomFieldValue)Activator.CreateInstance(typeof(TransactionCustomFieldValue<>).MakeGenericType(b.OwnerType))!;
        if (b.ParentType != null) value.GetType().GetProperty("ParentId")!.SetValue(value, Property(owner, b.ParentProperty));
        return value;
    }
    private static readonly MethodInfo readMethod = typeof(CustomFieldTransactions).GetMethod(nameof(Read), BindingFlags.Static | BindingFlags.NonPublic)!;
    public static async Task<List<CustomFieldValue>> ValuesAsync(AppDbContext db, Guid company, CustomFieldScope scope, Guid? owner = null, List<Guid>? owners = null)
    {
        var values = new List<CustomFieldValue>();
        foreach (var b in Bindings.Where(x => x.Scope == scope)) values.AddRange(await (Task<List<CustomFieldValue>>)readMethod.MakeGenericMethod(b.OwnerType).Invoke(null, new object?[] { db, company, scope, owner, owners })!);
        return values.DistinctBy(v => (v.OwnerId, v.DefinitionId)).ToList();
    }
    private static async Task<List<CustomFieldValue>> Read<T>(AppDbContext db, Guid company, CustomFieldScope scope, Guid? owner, List<Guid>? owners) where T : class =>
        (await db.Set<TransactionCustomFieldValue<T>>().Where(v => v.CompanyId == company && (!owner.HasValue || v.RecordId == owner) && (owners == null || owners.Contains(v.RecordId)) && db.Set<CustomFieldDefinition>().Any(d => d.Id == v.DefinitionId && d.Scope == scope)).ToListAsync()).Cast<CustomFieldValue>().ToList();
    public static async Task<(bool Required, bool Locked)> StageAsync(AppDbContext db, CustomFieldBinding b, object owner, bool persisted = false)
    {
        if (owner is VendorPayment payment)
        {
            var paymentBatch = payment.GLBatchId.HasValue ? await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == payment.GLBatchId) : null;
            return (!payment.Reference.StartsWith("draft-", StringComparison.OrdinalIgnoreCase), paymentBatch?.Status is BatchStatus.Ready or BatchStatus.Posted);
        }
        var parent = await ParentAsync(db, b, owner); var root = parent ?? owner;
        if (root is GLJournalHeader journal) root = (persisted ? await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == journal.BatchId) : await db.GLBatches.FindAsync(journal.BatchId)) ?? (object)journal;
        var status = Property(root, "Status")?.ToString() ?? "";
        var batchId = Property(root, "GLBatchId") ?? Property(root, "GlBatchId") ?? Property(root, "ShipmentBatchId") ?? Property(root, "PostedGLBatchId");
        var batch = batchId is Guid id ? (persisted ? await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) : await db.GLBatches.FindAsync(id)) : null;
        // Several sub-ledgers set Posted before GL approval. The linked batch is authoritative until commitment.
        var posted = Property(root, "CommittedAt") != null || status is "Finalized" or "Shipped" or "Received" or "Void" or "Cancelled" or "Converted" or "PartiallyConverted" || (batch == null && (Property(root, "IsPosted") is true || status == "Posted"));
        if (root is GoodsReceipt or StockTransfer && batchId is Guid stockBatchId)
            posted |= await db.StockLedgers.AsNoTracking().AnyAsync(m => m.GLBatchId == stockBatchId && m.CommittedAt != null);
        var locked = posted || Property(root, "IsLocked") is true || (batch == null && status is "Ready" or "InReview" or "Approved") || batch?.Status is BatchStatus.Ready or BatchStatus.Posted;
        var required = root is GoodsReceipt || root is StockTransfer || root is InventoryAdjustmentDocument || root is InventoryEntryDocument || Property(root, "IsPosted") is true || status is not ("Draft" or "Rejected" or "Pending" or "") || batchId is Guid;
        if (root is GoodsReceiptDraft) required = false;
        return (required, locked);
    }
    private static readonly MethodInfo ownerReadMethod = typeof(CustomFieldTransactions).GetMethod(nameof(ReadOwners), BindingFlags.Static | BindingFlags.NonPublic)!;
    public static Task<List<object>> OwnersAsync(AppDbContext db, CustomFieldBinding b, Guid company, DateOnly from, DateOnly to, string reference) => (Task<List<object>>)ownerReadMethod.MakeGenericMethod(b.OwnerType).Invoke(null, new object[] { db, b, company, from, to, reference })!;
    private static async Task<List<object>> ReadOwners<T>(AppDbContext db, CustomFieldBinding b, Guid company, DateOnly from, DateOnly to, string reference) where T : class
    {
        var p = Expression.Parameter(typeof(T), "owner"); Expression predicate;
        if (b.ParentType == null) predicate = Expression.Equal(Expression.Property(p, "CompanyId"), Expression.Constant(company));
        else
        {
            var parents = (IQueryable<Guid>)typeof(CustomFieldTransactions).GetMethod(nameof(ParentIds), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(b.ParentType).Invoke(null, new object?[] { db, company, b.DateProperty == null ? b.ParentDate : null, b.ParentReference, from, to, reference })!;
            var parentKey = Expression.Property(p, b.ParentProperty!);
            predicate = parentKey.Type == typeof(Guid?)
                ? Expression.AndAlso(Expression.Property(parentKey, "HasValue"), Expression.Call(typeof(Queryable), nameof(Queryable.Contains), new[] { typeof(Guid) }, parents.Expression, Expression.Property(parentKey, "Value")))
                : Expression.Call(typeof(Queryable), nameof(Queryable.Contains), new[] { typeof(Guid) }, parents.Expression, parentKey);
        }
        var query = db.Set<T>().AsNoTracking().Where(Expression.Lambda<Func<T, bool>>(predicate, p));
        query = FilterRegisterQuery(query, b.DateProperty, b.ParentType == null ? b.ReferenceProperty : null, from, to, reference);
        var rows = await query.Take(20001).ToListAsync();
        if (rows.Count > 20000) throw new ArgumentException("Narrow the date range or reference search to at most 20,000 records.");
        return rows.Cast<object>().ToList();
    }
    private static IQueryable<Guid> ParentIds<T>(AppDbContext db, Guid company, string? date, string? referenceProperty, DateOnly from, DateOnly to, string reference) where T : class =>
        FilterRegisterQuery(db.Set<T>().Where(x => EF.Property<Guid>(x, "CompanyId") == company), date, referenceProperty, from, to, reference).Select(x => EF.Property<Guid>(x, "Id"));
    private static IQueryable<T> FilterRegisterQuery<T>(IQueryable<T> query, string? dateProperty, string? referenceProperty, DateOnly from, DateOnly to, string reference)
    {
        var p = Expression.Parameter(typeof(T), "record");
        if (dateProperty != null && typeof(T).GetProperty(dateProperty) != null)
        {
            var raw = Expression.Property(p, dateProperty); var type = Nullable.GetUnderlyingType(raw.Type) ?? raw.Type;
            var value = raw.Type == type ? (Expression)raw : Expression.Property(raw, "Value");
            if (type == typeof(DateOnly) || type == typeof(DateTime))
            {
                object lower = type == typeof(DateOnly) ? from : from.ToDateTime(TimeOnly.MinValue);
                object upper = type == typeof(DateOnly) ? to : to.ToDateTime(TimeOnly.MaxValue);
                Expression range = Expression.AndAlso(Expression.GreaterThanOrEqual(value, Expression.Constant(lower, type)), Expression.LessThanOrEqual(value, Expression.Constant(upper, type)));
                if (raw.Type != type) range = Expression.OrElse(Expression.Not(Expression.Property(raw, "HasValue")), range);
                query = query.Where(Expression.Lambda<Func<T, bool>>(range, p));
            }
        }
        if (!string.IsNullOrEmpty(reference) && referenceProperty != null && typeof(T).GetProperty(referenceProperty) != null)
        {
            var raw = Expression.Property(p, referenceProperty);
            if (raw.Type == typeof(string))
            {
                var contains = Expression.AndAlso(Expression.NotEqual(raw, Expression.Constant(null, typeof(string))), Expression.Call(raw, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(reference)));
                query = query.Where(Expression.Lambda<Func<T, bool>>(contains, p));
            }
        }
        return query;
    }
    public static Task<List<object>> ChildrenAsync(AppDbContext db, CustomFieldBinding binding, Guid parent) => (Task<List<object>>)typeof(CustomFieldTransactions).GetMethod(nameof(ReadChildren), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(binding.OwnerType).Invoke(null, new object[] { db, binding.ParentProperty!, parent })!;
    private static async Task<List<object>> ReadChildren<T>(AppDbContext db, string property, Guid id) where T : class => (await db.Set<T>().Where(x => EF.Property<Guid>(x, property) == id).ToListAsync()).Cast<object>().ToList();
    public static async Task<(bool Required, bool Locked)> PreviousStageAsync(AppDbContext db, CustomFieldBinding binding, object owner)
    {
        var saved = db.Entry(owner).State == EntityState.Added ? null : await db.Entry(owner).GetDatabaseValuesAsync();
        if (saved == null) return (false, false);
        var root = saved.ToObject();
        if (binding.ParentType != null && owner is not VendorPayment)
        {
            var parent = await ParentAsync(db, binding, owner);
            var parentSaved = parent == null || db.Entry(parent).State == EntityState.Added ? null : await db.Entry(parent).GetDatabaseValuesAsync();
            if (parentSaved == null) return (false, false);
            root = parentSaved.ToObject();
        }
        return await StageAsync(db, binding with { ParentType = null, ParentProperty = null }, root, persisted: true);
    }
    public static async Task<bool> CommittedAsync(AppDbContext db, CustomFieldBinding binding, object owner)
    {
        var parent = await ParentAsync(db, binding, owner);
        object root = owner;
        if (parent != null)
        {
            var savedParent = db.Entry(parent).State == EntityState.Added ? null : await db.Entry(parent).GetDatabaseValuesAsync();
            if (savedParent == null) return false;
            root = savedParent.ToObject();
        }
        if (root is GLJournalHeader journal) root = await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == journal.BatchId) ?? (object)journal;
        var stage = await StageAsync(db, binding with { ParentType = null, ParentProperty = null }, root, persisted: true);
        var batchId = Property(root, "GLBatchId") ?? Property(root, "GlBatchId") ?? Property(root, "ShipmentBatchId") ?? Property(root, "PostedGLBatchId");
        if (batchId is Guid id)
        {
            if (root is GoodsReceipt or StockTransfer && await db.StockLedgers.AsNoTracking().AnyAsync(m => m.GLBatchId == id && m.CommittedAt != null)) return true;
            return Property(root, "CommittedAt") != null || await db.GLBatches.AsNoTracking().AnyAsync(x => x.Id == id && x.Status == BatchStatus.Posted);
        }
        return stage.Locked && Property(root, "Status")?.ToString() is not ("Ready" or "InReview" or "Approved");
    }
}
