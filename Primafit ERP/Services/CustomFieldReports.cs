using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed class CustomFieldReportFilter
{
    public bool MissingRequiredOnly { get; set; }
    public string Reference { get; set; } = "";
    public Guid? FieldId { get; set; }
    public string Value { get; set; } = "";
    public List<Guid> Columns { get; set; } = new();
}

public sealed partial class CustomFieldService
{
    public async Task<List<CustomFieldDefinition>> GetReportDefinitionsAsync(Guid company, CustomFieldScope scope)
    {
        await using var db = await factory.CreateDbContextAsync();
        await RequireAsync(db, company, scope, "CUSTOM_FIELDS_REPORTS"); await RequireAsync(db, company, scope, "CUSTOM_FIELDS_VIEW");
        return await Definitions(db, company, scope);
    }
    public static string Format(CustomFieldInput input, CustomFieldDataType type, IEnumerable<CustomFieldOption>? options = null, int places = 4) => type switch
    {
        CustomFieldDataType.Decimal => input.DecimalValue?.ToString($"F{places}", CultureInfo.CurrentCulture) ?? "Not captured",
        CustomFieldDataType.Date => input.DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not captured",
        CustomFieldDataType.Boolean => input.BooleanValue.HasValue ? input.BooleanValue.Value ? "Yes" : "No" : "Not captured",
        CustomFieldDataType.SingleSelect => options?.FirstOrDefault(x => x.Id == input.OptionId)?.Label ?? "Not captured",
        _ => string.IsNullOrWhiteSpace(input.TextValue) ? "Not captured" : input.TextValue
    };
    private sealed record OwnerRow(Guid Id, string Reference, string Name, DateOnly? Date, string Status);
    public async Task<StandardReportData> GenerateRegisterAsync(Guid company, CustomFieldScope scope, DateOnly from, DateOnly to, CustomFieldReportFilter filter)
    {
        if (from > to) throw new ArgumentException("The start date must be on or before the end date.");
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(db, company, scope, "CUSTOM_FIELDS_REPORTS"); await RequireAsync(db, company, scope, "CUSTOM_FIELDS_VIEW");
        var definitions = await Definitions(db, company, scope);
        var selected = definitions.Where(x => filter.Columns.Contains(x.Id) && x.Revisions.Single(r => r.RevisionNumber == x.CurrentRevision).IsReportable).OrderBy(x => x.Revisions.Single(r => r.RevisionNumber == x.CurrentRevision).DisplayOrder).ThenBy(x => x.Code).ToList();
        if (filter.Columns.Any(id => !selected.Any(x => x.Id == id))) throw new ArgumentException("A selected field is not available for report/export.");
        CustomFieldDefinition? filterDef = null;
        if (filter.FieldId.HasValue)
        {
            filterDef = definitions.SingleOrDefault(x => x.Id == filter.FieldId.Value && x.Revisions.Single(r => r.RevisionNumber == x.CurrentRevision).IsFilterable);
            if (filterDef == null) throw new ArgumentException("This field is not available for filtering.");
            ValidateFilter(filterDef, filter.Value);
        }
        List<OwnerRow> owners;
        if (scope == CustomFieldScope.Customer) owners = await db.Customers.Where(x => x.CompanyId == company && (filter.Reference == "" || x.Code.Contains(filter.Reference) || x.Name.Contains(filter.Reference))).Select(x => new OwnerRow(x.Id, x.Code, x.Name, null, "Master record")).Take(20001).ToListAsync();
        else if (scope == CustomFieldScope.Vendor) owners = await db.Vendors.Where(x => x.CompanyId == company && (filter.Reference == "" || x.Code.Contains(filter.Reference) || x.Name.Contains(filter.Reference))).Select(x => new OwnerRow(x.Id, x.Code, x.Name, null, "Master record")).Take(20001).ToListAsync();
        else if (scope == CustomFieldScope.Item) owners = await db.Items.Where(x => x.CompanyId == company && (filter.Reference == "" || x.Code.Contains(filter.Reference) || x.Name.Contains(filter.Reference))).Select(x => new OwnerRow(x.Id, x.Code, x.Name, null, "Master record")).Take(20001).ToListAsync();
        else if (CustomFieldTransactions.Supports(scope))
        {
            owners = new();
            foreach (var binding in CustomFieldTransactions.Bindings.Where(b => b.Scope == scope))
            {
                foreach (var owner in await CustomFieldTransactions.OwnersAsync(db, binding, company, from, to, filter.Reference))
                {
                    if (!await CustomFieldTransactions.AcceptAsync(db, binding, owner)) continue;
                    var parent = await CustomFieldTransactions.ParentAsync(db, binding, owner);
                    var reference = (parent == null ? CustomFieldTransactions.Property(owner, binding.ReferenceProperty) : CustomFieldTransactions.Property(parent, binding.ParentReference))?.ToString() ?? CustomFieldTransactions.Id(owner).ToString();
                    var rawDate = CustomFieldTransactions.Property(owner, binding.DateProperty) ?? (parent == null ? null : CustomFieldTransactions.Property(parent, binding.ParentDate));
                    DateOnly? date = rawDate switch { DateOnly d => d, DateTime d => DateOnly.FromDateTime(d), _ => null };
                    if (date.HasValue && (date < from || date > to) || filter.Reference.Length > 0 && !reference.Contains(filter.Reference, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = CustomFieldTransactions.Property(owner, "Description")?.ToString() ?? "";
                    if (CustomFieldTransactions.Property(owner, "ItemId") is Guid itemId) name = await db.Items.Where(i => i.Id == itemId && i.CompanyId == company).Select(i => i.Name).FirstOrDefaultAsync() ?? name;
                    else if (CustomFieldTransactions.Property(owner, "CustomerId") is Guid customerId) name = await db.Customers.Where(c => c.Id == customerId && c.CompanyId == company).Select(c => c.Name).FirstOrDefaultAsync() ?? name;
                    else if (CustomFieldTransactions.Property(owner, "VendorId") is Guid vendorId) name = await db.Vendors.Where(v => v.Id == vendorId && v.CompanyId == company).Select(v => v.Name).FirstOrDefaultAsync() ?? name;
                    var stage = await CustomFieldTransactions.StageAsync(db, binding, owner);
                    owners.Add(new OwnerRow(CustomFieldTransactions.Id(owner), reference, name, date, (CustomFieldTransactions.Property(parent ?? owner, "Status")?.ToString() ?? (stage.Locked ? "Committed" : stage.Required ? "Submitted" : "Draft"))));
                }
            }
            owners = owners.DistinctBy(o => o.Id).ToList();
        }
        else
        {
            var orders = db.SalesOrders.Where(x => x.CompanyId == company && x.Date >= from && x.Date <= to && !x.IsDirectInvoice && !x.InvoiceBatchId.HasValue && x.Status != OrderStatus.Quote && x.Status != OrderStatus.Draft && (filter.Reference == "" || x.OrderNumber.Contains(filter.Reference)));
            if (scope == CustomFieldScope.SalesOrderHeader) owners = await orders.Select(x => new OwnerRow(x.Id, x.OrderNumber, x.Customer!.Name, x.Date, x.SubmissionState == SalesOrderSubmissionState.Draft ? "Draft · incomplete allowed" : "Submitted")).Take(20001).ToListAsync();
            else owners = await db.SalesOrderLines.Where(l => orders.Any(o => o.Id == l.HeaderId)).Select(l => new OwnerRow(l.Id, l.Header!.OrderNumber, l.Item != null ? l.Item.Name : l.Description ?? "", l.Header.Date, l.Header.SubmissionState == SalesOrderSubmissionState.Draft ? "Draft · incomplete allowed" : "Submitted")).Take(20001).ToListAsync();
        }
        if (owners.Count > 20000) throw new ArgumentException("Narrow the date range or reference search to at most 20,000 records.");
        var ownerIds = owners.Select(x => x.Id).ToHashSet();
        var values = (await Values(db, company, scope, owners: ownerIds.ToList())).ToDictionary(x => (x.OwnerId, x.DefinitionId));
        var result = new StandardReportData { ReportName = filter.MissingRequiredOnly ? "Missing Required Custom Fields" : "Custom Field Register", ReportingPeriod = ScopeLabel(scope) + (!IsMasterScope(scope) ? $" · {from:dd MMM yyyy} to {to:dd MMM yyyy}" : " · Current master records"), Headers = new() { "Reference", "Name / item", "Date", "Workflow status" }, Columns = new() { new(ReportColumnKind.Text), new(ReportColumnKind.Text), new(ReportColumnKind.Date), new(ReportColumnKind.Text) } };
        if (scope.ToString().EndsWith("Line", StringComparison.Ordinal)) { result.Headers.Add("Line ID"); result.Columns.Add(new(ReportColumnKind.Text)); }
        foreach (var d in selected)
        {
            var r = d.Revisions.Single(x => x.RevisionNumber == d.CurrentRevision); result.Headers.Add(r.FieldName + (d.IsActive ? "" : " (inactive)"));
            result.Columns.Add(new(d.DataType switch { CustomFieldDataType.Decimal => ReportColumnKind.Number, CustomFieldDataType.Date => ReportColumnKind.Date, CustomFieldDataType.Boolean => ReportColumnKind.Boolean, _ => ReportColumnKind.Text }, r.DecimalPlaces, r.IncludeInExports));
        }
        foreach (var owner in owners.OrderBy(x => x.Reference).ThenBy(x => x.Id))
        {
            if (filter.MissingRequiredOnly && !definitions.Any(d => d.IsActive && d.Revisions.Single(r => r.RevisionNumber == d.CurrentRevision).Required && !values.ContainsKey((owner.Id, d.Id)))) continue;
            if (filterDef != null && (!values.TryGetValue((owner.Id, filterDef.Id), out var match) || !Matches(filterDef, match, filter.Value))) continue;
            var row = new List<string> { owner.Reference, owner.Name, owner.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", owner.Status };
            if (scope.ToString().EndsWith("Line", StringComparison.Ordinal)) row.Add(owner.Id.ToString());
            foreach (var d in selected)
            {
                if (!values.TryGetValue((owner.Id, d.Id), out var v)) { row.Add("Not captured"); continue; }
                row.Add(d.DataType == CustomFieldDataType.SingleSelect ? v.OptionLabelSnapshot ?? "Not captured" : Format(CustomFieldInput.From(v), d.DataType, null, d.Revisions.Single(r => r.Id == v.RevisionId).DecimalPlaces));
            }
            result.Rows.Add(row);
        }
        return result;
    }
    private static void ValidateFilter(CustomFieldDefinition d, string value)
    {
        if (d.DataType == CustomFieldDataType.Decimal && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out _)) throw new ArgumentException("Enter a valid decimal filter.");
        if (d.DataType == CustomFieldDataType.Date && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new ArgumentException("Enter a date filter as yyyy-MM-dd.");
        if (d.DataType == CustomFieldDataType.Boolean && !new[] { "yes", "no", "true", "false" }.Contains(value.ToLowerInvariant())) throw new ArgumentException("Enter Yes or No for this filter.");
    }
    private static bool Matches(CustomFieldDefinition d, CustomFieldValue value, string filter) => d.DataType switch {
        CustomFieldDataType.Decimal => value.DecimalValue == decimal.Parse(filter, CultureInfo.CurrentCulture),
        CustomFieldDataType.Date => value.DateValue == DateOnly.ParseExact(filter, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        CustomFieldDataType.Boolean => value.BooleanValue == (filter.Equals("yes", StringComparison.OrdinalIgnoreCase) || filter.Equals("true", StringComparison.OrdinalIgnoreCase)),
        CustomFieldDataType.SingleSelect => (value.OptionLabelSnapshot ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase),
        _ => (value.TextValue ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)
    };
}
