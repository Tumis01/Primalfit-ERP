using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;

namespace PrimafitERP.Data;

public static class TransactionCustomFieldModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<InventoryAdjustmentDocument>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<InventoryAdjustmentDocument>().HasOne<GLBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.GLBatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InventoryEntryDocument>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<InventoryEntryDocument>().HasOne<GLBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.GLBatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var binding in CustomFieldTransactions.Bindings.DistinctBy(x => x.OwnerType))
        {
            var valueType = typeof(TransactionCustomFieldValue<>).MakeGenericType(binding.OwnerType);
            var table = "Udf" + binding.OwnerType.Name + "Values";
            var value = builder.Entity(valueType);
            value.ToTable(table, t => t.HasCheckConstraint("CK_" + table + "_Value", "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DecimalValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1"));
            value.Ignore("OwnerId"); value.HasIndex("CompanyId", "RecordId", "DefinitionId").IsUnique();
            value.HasOne(typeof(CustomFieldRevision), null).WithMany().HasForeignKey("CompanyId", "DefinitionId", "RevisionId").HasPrincipalKey("CompanyId", "DefinitionId", "Id").OnDelete(DeleteBehavior.Restrict);
            value.HasOne(typeof(CustomFieldOption), null).WithMany().HasForeignKey("CompanyId", "DefinitionId", "OptionId").HasPrincipalKey("CompanyId", "DefinitionId", "Id").OnDelete(DeleteBehavior.Restrict);
            if (binding.ParentType == null)
            {
                builder.Entity(binding.OwnerType).HasAlternateKey("CompanyId", "Id");
                value.HasOne(binding.OwnerType, null).WithMany().HasForeignKey("CompanyId", "RecordId").HasPrincipalKey("CompanyId", "Id").OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                value.ToTable(table, t => t.HasCheckConstraint("CK_" + table + "_Parent", "[ParentId] IS NOT NULL"));
                builder.Entity(binding.ParentType).HasAlternateKey("CompanyId", "Id");
                builder.Entity(binding.OwnerType).HasAlternateKey(binding.ParentProperty!, "Id");
                value.HasOne(binding.ParentType, null).WithMany().HasForeignKey("CompanyId", "ParentId").HasPrincipalKey("CompanyId", "Id").OnDelete(DeleteBehavior.Restrict);
                value.HasOne(binding.OwnerType, null).WithMany().HasForeignKey("ParentId", "RecordId").HasPrincipalKey(binding.ParentProperty!, "Id").OnDelete(DeleteBehavior.Cascade);
            }
        }
    }
}
