using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed class DimensionReportService(IDbContextFactory<AppDbContext> factory)
{
    public async Task<Dictionary<Guid, string>> GetCaptionsAsync(Guid companyId, IReadOnlyCollection<Guid> setIds)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await (from selection in db.DimensionSetValues.AsNoTracking()
            join value in db.DimensionValues.AsNoTracking() on selection.DimensionValueId equals value.Id
            join type in db.DimensionTypes.AsNoTracking() on selection.DimensionTypeId equals type.Id
            where selection.CompanyId == companyId && value.CompanyId == companyId && type.CompanyId == companyId && setIds.Contains(selection.DimensionSetId)
            orderby type.DisplayOrder, type.Name
            select new { selection.DimensionSetId, TypeName = type.Name, ValueName = value.Name }).ToListAsync();
        return rows.GroupBy(x => x.DimensionSetId).ToDictionary(x => x.Key, x => string.Join(" · ", x.Select(v => $"{v.TypeName}: {v.ValueName}")));
    }

    public async Task<List<GLTransaction>> GetTransactionsAsync(Guid companyId, DateOnly from, DateOnly to,
        IReadOnlyCollection<Guid> valueIds, bool includeOpening = false)
    {
        if (from > to) throw new ArgumentException("The start date must be on or before the end date.");
        await using var db = await factory.CreateDbContextAsync();
        var query = db.GLTransactions.AsNoTracking().Include(x => x.SegCoa)
            .Where(x => x.CompanyId == companyId && x.PostingDate <= to);
        if (!includeOpening) query = query.Where(x => x.PostingDate >= from);
        query = await ApplyFiltersAsync(db, companyId, query, valueIds);
        return await query.OrderBy(x => x.PostingDate).ThenBy(x => x.CreatedAt).ToListAsync();
    }

    // Reuse the Finance statement layout and calculations with posted dimension activity.
    public static async Task<IQueryable<GLTransaction>> ApplyFiltersAsync(AppDbContext db, Guid companyId,
        IQueryable<GLTransaction> query, IReadOnlyCollection<Guid> valueIds)
    {
        if (valueIds.Distinct().Count() != await db.DimensionValues.CountAsync(x => x.CompanyId == companyId && valueIds.Contains(x.Id)))
            throw new ArgumentException("A report dimension is unavailable for this company.");
        query = query.Where(x => x.CompanyId == companyId
            && db.GLBatches.Any(b => b.Id == x.BatchId && b.CompanyId == companyId && b.Status == BatchStatus.Posted));
        foreach (var valueId in valueIds)
            query = query.Where(x => x.DimensionSetId.HasValue && db.DimensionSetValues.Any(v => v.CompanyId == companyId
                && v.DimensionSetId == x.DimensionSetId && v.DimensionValueId == valueId));
        return query;
    }
}
