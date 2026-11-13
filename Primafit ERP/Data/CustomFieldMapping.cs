using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

public static class CustomFieldMapping
{
    public static void Configure(ModelBuilder b)
    {
        b.Ignore<CustomFieldValue>();
        b.Entity<CustomFieldDefinition>().HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Entity<CustomFieldDefinition>().HasIndex(x => new { x.CompanyId, x.Scope, x.Code }).IsUnique();
        b.Entity<CustomFieldDefinition>().HasMany(x => x.Revisions).WithOne().HasForeignKey(x => new { x.CompanyId, x.DefinitionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CustomFieldDefinition>().HasMany(x => x.Options).WithOne().HasForeignKey(x => new { x.CompanyId, x.DefinitionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CustomFieldRevision>().HasAlternateKey(x => new { x.CompanyId, x.DefinitionId, x.Id });
        b.Entity<CustomFieldRevision>().HasIndex(x => new { x.CompanyId, x.DefinitionId, x.RevisionNumber }).IsUnique();
        b.Entity<CustomFieldOption>().HasAlternateKey(x => new { x.CompanyId, x.DefinitionId, x.Id });
        b.Entity<Customer>().HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Entity<Vendor>().HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Entity<Item>().HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Entity<SalesOrder>().HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Entity<SalesOrderLine>().HasAlternateKey(x => new { x.HeaderId, x.Id });
        Value(b.Entity<CustomerCustomFieldValue>(), "CustomerCustomFieldValues", "CustomerId");
        Value(b.Entity<VendorCustomFieldValue>(), "VendorCustomFieldValues", "VendorId");
        Value(b.Entity<ItemCustomFieldValue>(), "ItemCustomFieldValues", "ItemId");
        Value(b.Entity<SalesOrderCustomFieldValue>(), "SalesOrderCustomFieldValues", "SalesOrderId");
        Value(b.Entity<SalesOrderLineCustomFieldValue>(), "SalesOrderLineCustomFieldValues", "SalesOrderLineId");
        b.Entity<CustomerCustomFieldValue>().HasOne<Customer>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CustomerId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<VendorCustomFieldValue>().HasOne<Vendor>().WithMany().HasForeignKey(x => new { x.CompanyId, x.VendorId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ItemCustomFieldValue>().HasOne<Item>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ItemId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SalesOrderCustomFieldValue>().HasOne<SalesOrder>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SalesOrderId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SalesOrderLineCustomFieldValue>().HasOne<SalesOrder>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SalesOrderId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SalesOrderLineCustomFieldValue>().HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => new { x.SalesOrderId, x.SalesOrderLineId }).HasPrincipalKey(x => new { x.HeaderId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
    private static void Value<T>(EntityTypeBuilder<T> e, string table, string owner) where T : CustomFieldValue
    {
        e.ToTable(table, t => t.HasCheckConstraint("CK_" + table + "_TypedValue", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1"));
        e.Ignore(x => x.OwnerId);
        e.HasIndex("CompanyId", owner, "DefinitionId").IsUnique();
        e.HasOne<CustomFieldRevision>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DefinitionId, x.RevisionId }).HasPrincipalKey(x => new { x.CompanyId, x.DefinitionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<CustomFieldOption>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DefinitionId, x.OptionId }).HasPrincipalKey(x => new { x.CompanyId, x.DefinitionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
