using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;

namespace PrimafitERP.Data;

internal static class DimensionPersistence
{
    internal static async Task ValidateAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var entries = db.ChangeTracker.Entries().ToList();
        if (entries.Any(x => x.Entity is DimensionSet or DimensionSetValue && x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Saved dimension combinations cannot be changed or deleted. Create a new selection for the transaction line.");
        var selectionsByCompany = new Dictionary<Guid, List<Guid?>>();
        foreach (var entry in entries.Where(x => x.State is EntityState.Added or EntityState.Modified && x.Metadata.FindProperty("DimensionSetId") != null))
        {
            var property = entry.Property("DimensionSetId");
            if (entry.State == EntityState.Modified && !property.IsModified) continue;
            if (entry.Entity is GLTransaction && entry.State == EntityState.Modified)
                throw new InvalidOperationException("Posted GL dimensions cannot be changed.");
            if (property.CurrentValue is not Guid setId) continue;
            var companyId = await ResolveCompanyAsync(db, entry, cancellationToken);
            if (companyId == Guid.Empty) throw new InvalidOperationException("The company for this dimension selection could not be determined.");
            if (!selectionsByCompany.TryGetValue(companyId, out var selections)) selectionsByCompany[companyId] = selections = new();
            selections.Add(setId);
        }
        foreach (var company in selectionsByCompany)
        {
            var error = await DimensionService.ValidateAsync(db, company.Key, company.Value, false);
            if (error.Length > 0) throw new InvalidOperationException(error);
        }
    }

    private static async Task<Guid> ResolveCompanyAsync(AppDbContext db, EntityEntry entry, CancellationToken ct)
    {
        if (entry.Metadata.FindProperty("CompanyId") != null && entry.Property("CompanyId").CurrentValue is Guid company) return company;
        foreach (var fk in entry.Metadata.GetForeignKeys().Where(x => x.PrincipalEntityType.ClrType != typeof(DimensionSet)
                     && x.Properties.Count == 1 && x.Properties[0].Name is "HeaderId" or "PurchaseOrderId" or "PurchaseRequestId" or "SalesQuoteId" or "GoodsReceiptId" or "GoodsReceiptDraftId" or "PurchaseInvoiceId" or "SalesInvoiceId" or "ShipmentId" or "CreditNoteId" or "DebitNoteId" or "ReceiptRefundId" or "VendorReturnId" or "StockTransferId" or "PayrollRunId" or "CashbookBatchId" or "CustomerPaymentId" or "BatchId"))
        {
            var key = entry.Property(fk.Properties[0].Name).CurrentValue;
            var principal = key == null ? null : await db.FindAsync(fk.PrincipalEntityType.ClrType, new[] { key }, ct);
            if (principal != null) return await ResolveCompanyAsync(db, db.Entry(principal), ct);
        }
        return Guid.Empty;
    }
}
