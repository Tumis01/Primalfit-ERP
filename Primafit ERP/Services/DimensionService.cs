using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed class DimensionService(IDbContextFactory<AppDbContext> factory)
{
    public async Task<string> SavePayrollSelectionAsync(Guid companyId, Guid itemId, Guid? setId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var source = await db.PayrollItems.AsNoTracking().Where(x => x.Id == itemId && x.PayrollRun!.CompanyId == companyId).Select(x => new { x.PayrollRunId }).SingleOrDefaultAsync();
        if (source == null) return "Payroll line not found.";
        var run = await db.PayrollRuns.FromSqlInterpolated($"SELECT * FROM [PayrollRuns] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {source.PayrollRunId} AND [CompanyId] = {companyId}").FirstAsync();
        var rejected = run.GLBatchId.HasValue && await db.GLBatches.AnyAsync(x => x.Id == run.GLBatchId && x.CompanyId == companyId && x.Status == BatchStatus.Rejected);
        if (run.Status != PayrollRunStatus.Draft && !rejected) return "Payroll dimensions are locked after submission. Return the accrual for correction before editing.";
        var error = await ValidateAsync(db, companyId, new[] { setId }, false);
        if (error.Length > 0) return error;
        var item = await db.PayrollItems.SingleAsync(x => x.Id == itemId);
        item.DimensionSetId = setId; await db.SaveChangesAsync(); await transaction.CommitAsync(); return "";
    }

    public async Task<List<DimensionType>> GetTypesAsync(Guid companyId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DimensionTypes.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();
    }

    public static async Task<List<(Guid? SetId, decimal Weight)>> PostedInvoiceWeightsAsync(AppDbContext db, Guid companyId, Guid? batchId,
        bool debit, string referencePrefix, IEnumerable<(Guid? SetId, decimal Weight)> fallback)
    {
        if (batchId.HasValue)
        {
            var rows = await db.GLTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && x.BatchId == batchId
                && x.Narration != null && x.Narration.StartsWith(referencePrefix) && (debit ? x.Debit > 0 : x.Credit > 0)
                && db.GLBatches.Any(b => b.Id == x.BatchId && b.CompanyId == companyId && b.Status == BatchStatus.Posted))
                .Select(x => new { x.DimensionSetId, Weight = debit ? x.Debit : x.Credit }).ToListAsync();
            if (rows.Count > 0) return rows.Select(x => (x.DimensionSetId, x.Weight)).ToList();
        }
        return fallback.ToList();
    }

    public async Task<List<DimensionValue>> GetValuesAsync(Guid companyId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DimensionValues.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync();
    }

    public async Task<string> SaveTypeAsync(DimensionType input)
    {
        input.Code = (input.Code ?? "").Trim(); input.Name = (input.Name ?? "").Trim();
        if (input.CompanyId == Guid.Empty || input.Code.Length is 0 or > 50 || input.Name.Length is 0 or > 200)
            return "Enter a company, code (up to 50 characters) and name (up to 200 characters).";
        await using var db = await factory.CreateDbContextAsync();
        if (await db.DimensionTypes.AnyAsync(x => x.CompanyId == input.CompanyId && x.Code == input.Code && x.Id != input.Id)) return "This dimension type code already exists.";
        var row = await db.DimensionTypes.SingleOrDefaultAsync(x => x.Id == input.Id && x.CompanyId == input.CompanyId);
        if (row == null && input.Id != Guid.Empty) return "Dimension type not found.";
        if (row == null) { row = new DimensionType { CompanyId = input.CompanyId }; db.DimensionTypes.Add(row); }
        row.Code = input.Code; row.Name = input.Name; row.IsActive = input.IsActive; row.IsRequired = input.IsRequired; row.DisplayOrder = input.DisplayOrder;
        try { await db.SaveChangesAsync(); input.Id = row.Id; return ""; }
        catch (DbUpdateException) { return "The type could not be saved. Check that its code is unique."; }
    }

    public async Task<string> SaveValueAsync(DimensionValue input)
    {
        input.Code = (input.Code ?? "").Trim(); input.Name = (input.Name ?? "").Trim();
        if (input.Code.Length is 0 or > 50 || input.Name.Length is 0 or > 200 || input.Description?.Length > 2000) return "Enter a valid code and name; description must be at most 2,000 characters.";
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.DimensionTypes.AnyAsync(x => x.Id == input.DimensionTypeId && x.CompanyId == input.CompanyId)) return "Select a dimension type belonging to this company.";
        if (await db.DimensionValues.AnyAsync(x => x.DimensionTypeId == input.DimensionTypeId && x.Code == input.Code && x.Id != input.Id)) return "This dimension code already exists within this type.";
        var row = await db.DimensionValues.SingleOrDefaultAsync(x => x.Id == input.Id && x.CompanyId == input.CompanyId);
        if (row == null && input.Id != Guid.Empty) return "Dimension not found.";
        if (row != null && row.DimensionTypeId != input.DimensionTypeId) return "An existing dimension cannot be moved to another type.";
        if (row == null) { row = new DimensionValue { CompanyId = input.CompanyId, DimensionTypeId = input.DimensionTypeId }; db.DimensionValues.Add(row); }
        row.Code = input.Code; row.Name = input.Name; row.Description = input.Description?.Trim(); row.IsActive = input.IsActive;
        try { await db.SaveChangesAsync(); input.Id = row.Id; return ""; }
        catch (DbUpdateException) { return "The dimension could not be saved. Check that its code is unique."; }
    }

    public async Task<string> DeleteTypeAsync(Guid companyId, Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.DimensionTypes.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id);
        if (row == null) return "Dimension type not found.";
        if (await db.DimensionValues.AnyAsync(x => x.DimensionTypeId == id)) return "This type has dimensions. Deactivate it to retain its history.";
        db.Remove(row);
        try { await db.SaveChangesAsync(); return ""; } catch (DbUpdateException) { return "This type is in use. Deactivate it instead."; }
    }

    public async Task<string> DeleteValueAsync(Guid companyId, Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.DimensionValues.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id);
        if (row == null) return "Dimension not found.";
        if (row.LegacyProjectId.HasValue || await db.DimensionSetValues.AnyAsync(x => x.DimensionValueId == id)) return "This dimension has saved selections or project history. Deactivate it instead.";
        db.Remove(row);
        try { await db.SaveChangesAsync(); return ""; } catch (DbUpdateException) { return "This dimension is in use. Deactivate it instead."; }
    }

    public async Task<Dictionary<Guid, Guid>> GetSelectionsAsync(Guid companyId, Guid? setId)
    {
        if (!setId.HasValue) return new();
        await using var db = await factory.CreateDbContextAsync();
        return await db.DimensionSetValues.AsNoTracking().Where(x => x.CompanyId == companyId && x.DimensionSetId == setId)
            .ToDictionaryAsync(x => x.DimensionTypeId, x => x.DimensionValueId);
    }

    public async Task<(string Error, Guid? SetId)> SaveSelectionsAsync(Guid companyId, IReadOnlyDictionary<Guid, Guid> selections, Guid? previousSetId = null)
    {
        if (companyId == Guid.Empty) return ("Company is missing.", null);
        if (selections.Count == 0) return ("", null);
        await using var db = await factory.CreateDbContextAsync();
        var ids = selections.Values.Distinct().ToList();
        var values = await db.DimensionValues.Where(x => x.CompanyId == companyId && ids.Contains(x.Id)).ToListAsync();
        var types = await db.DimensionTypes.Where(x => x.CompanyId == companyId && x.IsActive).Select(x => x.Id).ToListAsync();
        var previous = previousSetId.HasValue
            ? await db.DimensionSetValues.AsNoTracking().Where(x => x.CompanyId == companyId && x.DimensionSetId == previousSetId).ToDictionaryAsync(x => x.DimensionTypeId, x => x.DimensionValueId)
            : new Dictionary<Guid, Guid>();
        if (values.Count != selections.Count || selections.Any(x => (!types.Contains(x.Key) && previous.GetValueOrDefault(x.Key) != x.Value)
                || !values.Any(v => v.Id == x.Value && v.DimensionTypeId == x.Key && v.IsActive)))
            return ("Choose active dimensions from the correct company and type.", null);
        var set = new DimensionSet { CompanyId = companyId };
        set.Values = selections.Select(x => new DimensionSetValue { DimensionSetId = set.Id, CompanyId = companyId, DimensionTypeId = x.Key, DimensionValueId = x.Value }).ToList();
        db.DimensionSets.Add(set); await db.SaveChangesAsync();
        return ("", set.Id);
    }

    // Used by every submission and again at final GL approval. Drafts only check supplied selections.
    public static async Task<string> ValidateAsync(AppDbContext db, Guid companyId, IEnumerable<Guid?> lineSets, bool requireComplete)
    {
        var requested = lineSets.ToList(); var ids = requested.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var sets = await db.DimensionSets.AsNoTracking().Include(x => x.Values).Where(x => ids.Contains(x.Id) && x.CompanyId == companyId).ToListAsync();
        // Include sets created in the caller's transaction but not yet saved.
        foreach (var entry in db.ChangeTracker.Entries<DimensionSet>().Where(x => x.State == EntityState.Added && x.Entity.CompanyId == companyId && ids.Contains(x.Entity.Id)))
            if (sets.All(x => x.Id != entry.Entity.Id)) sets.Add(entry.Entity);
        if (sets.Count != ids.Count) return "A line contains dimensions belonging to another company or a missing selection. Select its dimensions again.";
        var selectedIds = sets.SelectMany(x => x.Values).Select(x => x.DimensionValueId).Distinct().ToList();
        var values = await db.DimensionValues.AsNoTracking().Where(x => x.CompanyId == companyId && selectedIds.Contains(x.Id)).ToListAsync();
        if (sets.SelectMany(x => x.Values).Any(x => !values.Any(v => v.Id == x.DimensionValueId && v.DimensionTypeId == x.DimensionTypeId && v.IsActive)))
            return "A selected dimension is inactive or unavailable. Update the line's dimensions.";
        if (!requireComplete) return "";
        var required = await db.DimensionTypes.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive && x.IsRequired).OrderBy(x => x.DisplayOrder).ToListAsync();
        for (var i = 0; i < requested.Count; i++)
        {
            var selection = sets.FirstOrDefault(x => x.Id == requested[i]);
            var missing = required.Where(t => selection == null || selection.Values.All(v => v.DimensionTypeId != t.Id)).Select(t => t.Name).ToList();
            if (missing.Count > 0) return $"Line {i + 1}: select the required dimensions: {string.Join(", ", missing)}.";
        }
        return "";
    }

    // Split document-level tax, discount and control legs using source-line weights.
    // Explicitly dimensioned item legs remain untouched; rounding residual goes to the final leg.
    public static void AllocateSummaryLines(List<GLJournalLine> lines, IEnumerable<(Guid? SetId, decimal Weight)> source)
    {
        var weights = source.GroupBy(x => x.SetId).Select(x => (SetId: x.Key, Weight: x.Sum(y => Math.Abs(y.Weight)))).Where(x => x.Weight > 0).ToList();
        if (weights.Count == 0) return;
        var total = weights.Sum(x => x.Weight);
        var summary = lines.Where(x => !x.DimensionSetId.HasValue && !x.IsDimensionDetail).ToList();
        var closing = lines.LastOrDefault();
        var balanceClosing = closing != null && summary.Contains(closing) && lines.Any(x => x.IsDimensionDetail)
            && lines.Sum(x => x.Debit) == lines.Sum(x => x.Credit);
        if (balanceClosing) { summary.Remove(closing!); lines.Remove(closing!); }
        foreach (var line in summary)
        {
            lines.Remove(line); decimal debit = 0, credit = 0;
            for (var i = 0; i < weights.Count; i++)
            {
                var last = i == weights.Count - 1;
                var d = last ? line.Debit - debit : Math.Min(line.Debit - debit, Math.Round(line.Debit * weights[i].Weight / total, 4, MidpointRounding.AwayFromZero));
                var c = last ? line.Credit - credit : Math.Min(line.Credit - credit, Math.Round(line.Credit * weights[i].Weight / total, 4, MidpointRounding.AwayFromZero));
                debit += d; credit += c;
                if (d != 0 || c != 0) lines.Add(new GLJournalLine { SegCoaId = line.SegCoaId, Debit = d, Credit = c, Reference = line.Reference, ProjectId = line.ProjectId, DimensionSetId = weights[i].SetId, IsDimensionDetail = true });
            }
        }
        if (balanceClosing)
        {
            var balances = lines.GroupBy(x => x.DimensionSetId).Select(x => new { SetId = x.Key, Net = x.Sum(l => l.Debit - l.Credit) }).ToList();
            if (balances.All(x => closing!.Debit > 0 ? x.Net <= 0 : x.Net >= 0))
            {
                foreach (var balance in balances.Where(x => x.Net != 0))
                    lines.Add(new GLJournalLine { SegCoaId = closing!.SegCoaId, Debit = Math.Max(0, -balance.Net), Credit = Math.Max(0, balance.Net),
                        Reference = closing.Reference, ProjectId = closing.ProjectId, DimensionSetId = balance.SetId, IsDimensionDetail = true });
            }
            else
            {
                var controls = new List<GLJournalLine> { closing! };
                AllocateSummaryLines(controls, weights);
                lines.AddRange(controls);
            }
        }
    }
}
