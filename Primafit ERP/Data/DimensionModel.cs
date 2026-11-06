using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

internal static class DimensionModel
{
    internal static void Configure(ModelBuilder builder)
    {
        builder.Entity<DimensionType>().HasAlternateKey(x => new { x.Id, x.CompanyId });
        builder.Entity<DimensionType>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        builder.Entity<DimensionValue>().HasAlternateKey(x => new { x.Id, x.DimensionTypeId, x.CompanyId });
        builder.Entity<DimensionValue>().HasIndex(x => new { x.DimensionTypeId, x.Code }).IsUnique();
        builder.Entity<DimensionValue>().HasIndex(x => x.LegacyProjectId).IsUnique().HasFilter("[LegacyProjectId] IS NOT NULL");
        builder.Entity<DimensionValue>().HasOne<DimensionType>().WithMany()
            .HasForeignKey(x => new { x.DimensionTypeId, x.CompanyId }).HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DimensionSet>().HasAlternateKey(x => new { x.Id, x.CompanyId });
        builder.Entity<DimensionSet>().HasMany(x => x.Values).WithOne()
            .HasForeignKey(x => new { x.DimensionSetId, x.CompanyId }).HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DimensionSetValue>().HasKey(x => new { x.DimensionSetId, x.DimensionTypeId });
        builder.Entity<DimensionSetValue>().HasOne<DimensionValue>().WithMany()
            .HasForeignKey(x => new { x.DimensionValueId, x.DimensionTypeId, x.CompanyId })
            .HasPrincipalKey(x => new { x.Id, x.DimensionTypeId, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var type in new[] { typeof(DimensionType), typeof(DimensionValue), typeof(DimensionSet) })
            builder.Entity(type).HasOne(typeof(CompanyDetails), null).WithMany().HasForeignKey("CompanyId")
                .HasPrincipalKey(nameof(CompanyDetails.CompanyDetailsId)).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in builder.Model.GetEntityTypes().Where(x => x.ClrType.GetProperty("DimensionSetId") != null
                     && x.ClrType != typeof(DimensionSetValue)).ToList())
            builder.Entity(entity.ClrType).HasOne(typeof(DimensionSet), null).WithMany().HasForeignKey("DimensionSetId").OnDelete(DeleteBehavior.Restrict);
    }
}
