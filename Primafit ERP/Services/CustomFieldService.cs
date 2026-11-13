using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed partial class CustomFieldService(IDbContextFactory<AppDbContext> factory, AuthenticationStateProvider authentication)
{
    public static string ScopeLabel(CustomFieldScope s) => s switch { CustomFieldScope.SalesOrderHeader => "Sales Order · Header", CustomFieldScope.SalesOrderLine => "Sales Order · Line", CustomFieldScope.JournalHeader => "General Ledger · Batch", CustomFieldScope.JournalLine => "General Ledger · Line", _ => CustomFieldTransactions.Supports(s) ? Regex.Replace(s.ToString(), "(?<!^)([A-Z])", " $1").Replace(" Header", " · Header").Replace(" Line", " · Line") : s + " · Master record" };
    public static string Module(CustomFieldScope s) => CustomFieldTransactions.Supports(s) ? CustomFieldTransactions.Binding(s).Module : s == CustomFieldScope.Vendor ? "Purchasing" : s == CustomFieldScope.Item ? "Inventory" : "Sales";
    public static string EditPermission(CustomFieldScope s) => CustomFieldTransactions.Supports(s) ? CustomFieldTransactions.Binding(s).EditPermission : s switch { CustomFieldScope.Customer => "CUSTOMER_GROUP_MANAGE", CustomFieldScope.Vendor => "VENDOR_MANAGE", CustomFieldScope.Item => "INVENTORY_SETUP_MANAGE", _ => "SALES_ORDER_CREATE" };
    public static string ViewPermission(CustomFieldScope s) => CustomFieldTransactions.Supports(s) ? CustomFieldTransactions.Binding(s).ViewPermission : s switch { CustomFieldScope.Customer => "MD_CUSTOMER_GROUPS_PAGE", CustomFieldScope.Vendor => "MD_VENDORS_PAGE", CustomFieldScope.Item => "INVENTORY_SETUP_PAGE", _ => "SALES_ORDER_PAGE" };
    public static bool IsMasterScope(CustomFieldScope scope) => scope is CustomFieldScope.Customer or CustomFieldScope.Vendor or CustomFieldScope.Item;

    public async Task<(string Id, HashSet<string> Permissions, bool Super)> ActorAsync(AppDbContext db, Guid company)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || id == null || !await db.Users.AnyAsync(x => x.Id == id && x.CompanyDetailsId == company))
            throw new UnauthorizedAccessException("The custom-field company is unavailable to this user.");
        var roleIds = await db.UserRoles.Where(x => x.UserId == id).Select(x => x.RoleId).ToListAsync();
        var roles = await db.Roles.Where(x => roleIds.Contains(x.Id)).ToListAsync();
        var super = roles.Any(x => x.Name != null && x.Name.Contains("SuperAdmin", StringComparison.Ordinal));
        var custom = await db.CompanyRolePermissions.Where(x => roleIds.Contains(x.ApplicationRoleId) && db.Roles.Any(r => r.Id == x.ApplicationRoleId && r.CompanyId == company)).Select(x => x.Permission.ActionKey ?? x.Permission.PageKey).ToListAsync();
        var templates = await db.UserSystemRoles.Where(x => x.UserId == id && x.CompanyId == company).Select(x => x.SystemRoleTemplateId).ToListAsync();
        var system = await db.SystemRolePermissions.Where(x => templates.Contains(x.SystemRoleTemplateId)).Select(x => x.Permission.ActionKey ?? x.Permission.PageKey).ToListAsync();
        return (id, custom.Concat(system).Where(x => x != null).Select(x => x!).ToHashSet(StringComparer.Ordinal), super);
    }
    public async Task<string> RequireAsync(AppDbContext db, Guid company, CustomFieldScope scope, string action)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentException("Unsupported custom-field scope.");
        var actor = await ActorAsync(db, company);
        var modulePermission = action == "CUSTOM_FIELDS_EDIT" ? EditPermission(scope) : ViewPermission(scope);
        if (!actor.Super && (!actor.Permissions.Contains(action) || !actor.Permissions.Contains(modulePermission))) throw new UnauthorizedAccessException("You do not have permission for these custom fields.");
        return actor.Id;
    }
    private static string Schema(IEnumerable<CustomFieldDefinition> defs) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", defs.OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.CurrentRevision}:{x.IsActive}:{Convert.ToBase64String(x.RowVersion)}")))));
    private static Task<List<CustomFieldDefinition>> Definitions(AppDbContext db, Guid company, CustomFieldScope scope) => db.Set<CustomFieldDefinition>().Include(x => x.Revisions).Include(x => x.Options).Where(x => x.CompanyId == company && x.Scope == scope).ToListAsync();

    public async Task<List<CustomFieldDefinition>> GetDefinitionsAsync(Guid company, CustomFieldScope scope)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(db, company, scope, "CUSTOM_FIELDS_SETUP");
        return (await Definitions(db, company, scope)).OrderBy(x => x.Revisions.Single(r => r.RevisionNumber == x.CurrentRevision).DisplayOrder).ThenBy(x => x.Code).ToList();
    }
    public async Task<string> PublishAsync(Guid company, CustomFieldScope scope, CustomFieldDefinitionEdit edit)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await RequireAsync(db, company, scope, "CUSTOM_FIELDS_SETUP");
        var input = edit.Definition; var revision = edit.Revision;
        var code = input.Code.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(code, "^[A-Z][A-Z0-9_]{0,59}$")) return "Code must start with a letter and contain only letters, numbers and underscores (maximum 60).";
        if (string.IsNullOrWhiteSpace(revision.Label) || revision.Label.Length > 100 || revision.HelpText.Length > 300 || revision.Section.Length > 80) return "Enter a label (up to 100 characters), section (up to 80) and help text (up to 300).";
        if (!Enum.IsDefined(input.DataType) || revision.MaxLength is < 1 or > 4000 || revision.DecimalPlaces is < 0 or > 4 || revision.Minimum > revision.Maximum) return "Check the field type, text length, decimal places and range.";
        if (edit.Options.Count > 100 || edit.Options.Any(x => string.IsNullOrWhiteSpace(x.Label) || x.Label.Trim().Length > 100) || edit.Options.GroupBy(x => x.Label.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return "Choices must be unique, non-empty labels of up to 100 characters; at most 100 choices are supported.";
        if (input.DataType == CustomFieldDataType.SingleSelect && !edit.Options.Any(x => x.IsActive)) return "Add at least one active dropdown choice.";
        var existing = await db.Set<CustomFieldDefinition>().Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == input.Id && x.CompanyId == company && x.Scope == scope);
        if (existing == null && await db.Set<CustomFieldDefinition>().AnyAsync(x => x.Id == input.Id)) return "Field not found in this scope.";
        if (await db.Set<CustomFieldDefinition>().AnyAsync(x => x.CompanyId == company && x.Scope == scope && x.Code == code && x.Id != input.Id)) return "This code is already used in this scope.";
        if (existing != null && (existing.Code != code || existing.DataType != input.DataType)) return "A published field's code and type cannot change. Create a new field instead.";
        if (existing != null && !existing.RowVersion.SequenceEqual(input.RowVersion)) return "This field was changed by another user. Refresh before publishing.";
        await using var tx = await db.Database.BeginTransactionAsync();
        var definition = existing ?? new CustomFieldDefinition { CompanyId = company, Scope = scope, Code = code, DataType = input.DataType, CreatedByUserId = actor };
        if (existing == null) db.Add(definition); else definition.CurrentRevision++;
        definition.IsActive = input.IsActive;
        var storedRevision = new CustomFieldRevision { CompanyId = company, DefinitionId = definition.Id, RevisionNumber = definition.CurrentRevision, Label = revision.Label.Trim(), HelpText = revision.HelpText.Trim(), Section = string.IsNullOrWhiteSpace(revision.Section) ? "Additional information" : revision.Section.Trim(), DisplayOrder = revision.DisplayOrder, Required = revision.Required, MaxLength = revision.MaxLength, DecimalPlaces = revision.DecimalPlaces, Minimum = revision.Minimum, Maximum = revision.Maximum, IncludeOnPrint = revision.IncludeOnPrint, IncludeInExports = revision.IncludeInExports, IsReportable = revision.IsReportable, IsFilterable = revision.IsFilterable, PublishedByUserId = actor };
        db.Add(storedRevision);
        foreach (var previous in definition.Options) previous.IsActive = false;
        foreach (var choice in edit.Options)
        {
            var option = definition.Options.FirstOrDefault(x => x.Id == choice.Id);
            if (option == null)
            {
                if (await db.Set<CustomFieldOption>().AnyAsync(x => x.Id == choice.Id)) return "A dropdown choice belongs to another field.";
                option = new CustomFieldOption { CompanyId = company, DefinitionId = definition.Id }; definition.Options.Add(option);
            }
            option.Label = choice.Label.Trim(); option.IsActive = choice.IsActive; option.DisplayOrder = choice.DisplayOrder;
        }
        db.AuditLogs.Add(new AuditLog { CompanyId = company, UserId = actor, Action = "CustomFieldPublished", EntityType = nameof(CustomFieldDefinition), EntityId = definition.Id, Details = JsonSerializer.Serialize(new { definition.Code, definition.Scope, definition.CurrentRevision, definition.IsActive, storedRevision.Label }) });
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); return ""; }
        catch (DbUpdateConcurrencyException) { return "The field changed while publishing. Refresh and try again."; }
        catch (DbUpdateException) { return "The field could not be published. Check its code and choices, then refresh."; }
    }

    private static async Task<List<CustomFieldValue>> Values(AppDbContext db, Guid company, CustomFieldScope scope, Guid? owner = null, List<Guid>? owners = null)
    {
        return scope switch
        {
            CustomFieldScope.Customer => (await db.Set<CustomerCustomFieldValue>().Where(x => x.CompanyId == company && (!owner.HasValue || x.CustomerId == owner.Value) && (owners == null || owners.Contains(x.CustomerId))).ToListAsync()).Cast<CustomFieldValue>().ToList(),
            CustomFieldScope.Vendor => (await db.Set<VendorCustomFieldValue>().Where(x => x.CompanyId == company && (!owner.HasValue || x.VendorId == owner.Value) && (owners == null || owners.Contains(x.VendorId))).ToListAsync()).Cast<CustomFieldValue>().ToList(),
            CustomFieldScope.Item => (await db.Set<ItemCustomFieldValue>().Where(x => x.CompanyId == company && (!owner.HasValue || x.ItemId == owner.Value) && (owners == null || owners.Contains(x.ItemId))).ToListAsync()).Cast<CustomFieldValue>().ToList(),
            CustomFieldScope.SalesOrderHeader => (await db.Set<SalesOrderCustomFieldValue>().Where(x => x.CompanyId == company && (!owner.HasValue || x.SalesOrderId == owner.Value) && (owners == null || owners.Contains(x.SalesOrderId))).ToListAsync()).Cast<CustomFieldValue>().ToList(),
            CustomFieldScope.SalesOrderLine => (await db.Set<SalesOrderLineCustomFieldValue>().Where(x => x.CompanyId == company && (!owner.HasValue || x.SalesOrderLineId == owner.Value) && (owners == null || owners.Contains(x.SalesOrderLineId))).ToListAsync()).Cast<CustomFieldValue>().ToList(),
            _ => await CustomFieldTransactions.ValuesAsync(db, company, scope, owner, owners)
        };
    }
    private static async Task<(object? Owner, Guid? Version)> FindOwner(AppDbContext db, Guid company, CustomFieldScope scope, Guid id)
    {
        object? owner = scope switch {
            CustomFieldScope.Customer => await db.Customers.FirstOrDefaultAsync(x => x.CompanyId == company && x.Id == id),
            CustomFieldScope.Vendor => await db.Vendors.FirstOrDefaultAsync(x => x.CompanyId == company && x.Id == id),
            CustomFieldScope.Item => await db.Items.FirstOrDefaultAsync(x => x.CompanyId == company && x.Id == id),
            CustomFieldScope.SalesOrderHeader => await db.SalesOrders.FirstOrDefaultAsync(x => x.CompanyId == company && x.Id == id && x.Status != OrderStatus.Quote && !x.IsDirectInvoice && !x.InvoiceBatchId.HasValue),
            CustomFieldScope.SalesOrderLine => await db.SalesOrderLines.Include(x => x.Header).Where(x => x.Header!.CompanyId == company && x.Id == id).Select(x => x.Header).FirstOrDefaultAsync(),
            _ => await CustomFieldTransactions.FindAsync(db, company, scope, id)
        };
        Guid? version = owner switch { Customer x => x.CustomFieldsVersion, Vendor x => x.CustomFieldsVersion, Item x => x.CustomFieldsVersion, SalesOrder x => x.CustomFieldsVersion, _ => owner == null ? null : (Guid?)CustomFieldTransactions.Property(owner, "CustomFieldsVersion") };
        return (owner, version);
    }
    public async Task<CustomFieldFormState> LoadEditorAsync(Guid company, CustomFieldScope scope, Guid ownerId, bool readOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ActorAsync(db, company);
        if (!Enum.IsDefined(scope)) throw new ArgumentException("Unsupported custom-field scope.");
        var definitions = await Definitions(db, company, scope);
        var form = new CustomFieldFormState { CompanyId = company, Scope = scope, OwnerId = ownerId, SchemaToken = Schema(definitions) };
        if (definitions.Count == 0) return form;
        await RequireAsync(db, company, scope, "CUSTOM_FIELDS_VIEW"); var actor = await ActorAsync(db, company);
        form.CanEdit = !readOnly && (actor.Super || actor.Permissions.Contains("CUSTOM_FIELDS_EDIT") && actor.Permissions.Contains(EditPermission(scope)));
        var owner = await FindOwner(db, company, scope, ownerId); form.ExpectedOwnerVersion = owner.Version;
        if (owner.Owner != null && CustomFieldTransactions.Supports(scope) && (await CustomFieldTransactions.StageAsync(db, CustomFieldTransactions.Binding(scope, owner.Owner), owner.Owner)).Locked) form.CanEdit = false;
        var values = owner.Owner == null ? new List<CustomFieldValue>() : await Values(db, company, scope, ownerId);
        if (owner.Owner is SalesOrder so && (so.Status == OrderStatus.Invoiced || so.Lines.Any(x => x.QtyShipped > 0))) form.CanEdit = false;
        foreach (var def in definitions.Where(x => x.IsActive || values.Any(v => v.DefinitionId == x.Id)))
        {
            var value = values.SingleOrDefault(x => x.DefinitionId == def.Id);
            var revision = readOnly && value != null ? def.Revisions.Single(x => x.Id == value.RevisionId) : def.Revisions.Single(x => x.RevisionNumber == def.CurrentRevision);
            var options = def.Options.Where(x => x.IsActive || x.Id == value?.OptionId).Select(x => new CustomFieldOption { Id = x.Id, Label = x.Id == value?.OptionId ? value.OptionLabelSnapshot ?? x.Label : x.Label, IsActive = x.IsActive }).ToList();
            form.Fields.Add(new() { Definition = def, Revision = revision, Input = value == null ? new() { DefinitionId = def.Id } : CustomFieldInput.From(value), Options = options });
        }
        form.Fields = form.Fields.OrderBy(x => x.Revision.DisplayOrder).ThenBy(x => x.Revision.Label).ToList(); return form;
    }

    public static async Task<string> UnavailableAsync(AppDbContext db, Guid company, params CustomFieldScope[] scopes) => await db.Set<CustomFieldDefinition>().AnyAsync(x => x.CompanyId == company && scopes.Contains(x.Scope) && x.IsActive) ? "This operation requires the authenticated custom-field document editor." : "";

    public async Task<string> ApplyAsync(AppDbContext db, Guid company, CustomFieldScope scope, Guid ownerId, object aggregate, CustomFieldFormState? form, bool requireFields)
    {
        try { return await ApplyCoreAsync(db, company, scope, ownerId, aggregate, form, requireFields); }
        catch (UnauthorizedAccessException ex) { return ex.Message; }
    }
    private async Task<string> ApplyCoreAsync(AppDbContext db, Guid company, CustomFieldScope scope, Guid ownerId, object aggregate, CustomFieldFormState? form, bool requireFields)
    {
        if (!string.IsNullOrEmpty(form?.Error)) return form.Error;
        var defs = await Definitions(db, company, scope);
        if (defs.Count == 0 && form?.Fields.Count is null or 0) return "";
        var actor = await RequireAsync(db, company, scope, "CUSTOM_FIELDS_VIEW");
        if (form != null && (form.CompanyId != company || form.Scope != scope || form.OwnerId != ownerId && !(form.OwnerId == Guid.Empty && db.Entry(aggregate).State == EntityState.Added))) return "The custom fields belong to another record.";
        if (form != null && Schema(defs) != form.SchemaToken) return form.Error = "Custom-field setup changed while editing. Refresh the document before saving.";
        var existing = await Values(db, company, scope, ownerId);
        var inputs = form?.Fields.Select(x => x.Input).ToList() ?? existing.Select(CustomFieldInput.From).ToList();
        if (CustomFieldTransactions.Supports(scope)) existing = existing.Where(v => v.GetType() == typeof(TransactionCustomFieldValue<>).MakeGenericType(aggregate.GetType())).ToList();
        if (inputs.GroupBy(x => x.DefinitionId).Any(x => x.Count() > 1) || inputs.Any(x => !defs.Any(d => d.Id == x.DefinitionId))) return "Invalid or duplicate custom field.";
        var actorInfo = await ActorAsync(db, company); var canEdit = actorInfo.Super || actorInfo.Permissions.Contains("CUSTOM_FIELDS_EDIT") && actorInfo.Permissions.Contains(EditPermission(scope));
        if (!actorInfo.Super && !actorInfo.Permissions.Contains(EditPermission(scope))) return "You do not have permission to edit this record.";
        bool changed = false;
        foreach (var def in defs)
        {
            var revision = def.Revisions.Single(x => x.RevisionNumber == def.CurrentRevision);
            var value = existing.SingleOrDefault(x => x.DefinitionId == def.Id);
            var input = inputs.SingleOrDefault(x => x.DefinitionId == def.Id) ?? new CustomFieldInput { DefinitionId = def.Id };
            var error = Validate(def, revision, input, value, requireFields);
            if (form != null) { var field = form.Fields.SingleOrDefault(x => x.Definition.Id == def.Id); if (field != null) field.Error = error; }
            if (error != null) return form == null ? error : form.Error = error;
            var same = value == null ? input.IsEmpty : Equal(input, CustomFieldInput.From(value));
            if (same && (value == null || value.RevisionId == revision.Id || !def.IsActive)) continue;
            if (!canEdit) { if (!same) return "You cannot edit custom-field values."; continue; }
            if (scope is CustomFieldScope.SalesOrderHeader or CustomFieldScope.SalesOrderLine && aggregate is SalesOrder order && (order.IsInvoiceDocument || order.Status == OrderStatus.Quote || order.Lines.Any(x => x.QtyShipped > 0))) return "These order custom fields are read-only.";
            var old = value == null ? null : CustomFieldInput.From(value);
            if (input.IsEmpty) { if (value != null) db.Remove(value); }
            else
            {
                if (value == null) { value = NewValue(scope, aggregate); value.CompanyId = company; value.OwnerId = ownerId; value.DefinitionId = def.Id; db.Add(value); }
                value.RevisionId = revision.Id; value.TextValue = string.IsNullOrWhiteSpace(input.TextValue) ? null : input.TextValue.Trim(); value.DecimalValue = input.DecimalValue; value.DateValue = input.DateValue; value.BooleanValue = input.BooleanValue; value.OptionId = input.OptionId;
                value.OptionLabelSnapshot = input.OptionId.HasValue ? value.OptionId == old?.OptionId ? value.OptionLabelSnapshot : def.Options.Single(x => x.Id == input.OptionId).Label : null;
                value.UpdatedAt = DateTime.UtcNow; value.UpdatedByUserId = actor;
            }
            db.AuditLogs.Add(new AuditLog { CompanyId = company, UserId = actor, Action = "CustomFieldValueChanged", EntityType = scope.ToString(), EntityId = ownerId, Details = JsonSerializer.Serialize(new { def.Code, Before = old, After = input }) }); changed = true;
        }
        // The aggregate token covers edits to both standard and custom fields.
        var entry = db.Entry(aggregate); var token = entry.Property("CustomFieldsVersion");
        var stored = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync();
        var current = stored?.GetValue<Guid?>("CustomFieldsVersion");
        if (form != null && entry.State != EntityState.Added && form.ExpectedOwnerVersion != current) return form.Error = "This record changed while you were editing. Refresh before saving.";
        token.OriginalValue = current;
        if (changed || form != null) token.CurrentValue = Guid.NewGuid();
        else token.CurrentValue = current;
        return "";
    }
    private static CustomFieldValue NewValue(CustomFieldScope scope, object owner) => scope switch {
        CustomFieldScope.Customer => new CustomerCustomFieldValue(), CustomFieldScope.Vendor => new VendorCustomFieldValue(), CustomFieldScope.Item => new ItemCustomFieldValue(),
        CustomFieldScope.SalesOrderHeader => new SalesOrderCustomFieldValue(), CustomFieldScope.SalesOrderLine => new SalesOrderLineCustomFieldValue { SalesOrderId = ((SalesOrder)owner).Id }, _ => CustomFieldTransactions.NewValue(scope, owner) };
    public static bool SameInput(CustomFieldInput a, CustomFieldInput b) => Equal(a, b);
    private static bool Equal(CustomFieldInput a, CustomFieldInput b) => (a.TextValue ?? "").Trim() == (b.TextValue ?? "").Trim() && a.DecimalValue == b.DecimalValue && a.DateValue == b.DateValue && a.BooleanValue == b.BooleanValue && a.OptionId == b.OptionId;
    private static string? Validate(CustomFieldDefinition d, CustomFieldRevision r, CustomFieldInput input, CustomFieldValue? old, bool required)
    {
        if (!d.IsActive) return old == null && !input.IsEmpty || old != null && !Equal(input, CustomFieldInput.From(old)) ? $"{r.Label} is inactive; preserve its saved value." : null;
        if (input.IsEmpty) return required && r.Required ? $"Complete {r.Label}." : null;
        var typed = new[] { !string.IsNullOrWhiteSpace(input.TextValue), input.DecimalValue.HasValue, input.DateValue.HasValue, input.BooleanValue.HasValue, input.OptionId.HasValue };
        var typeIndex = d.DataType switch { CustomFieldDataType.ShortText or CustomFieldDataType.LongText => 0, CustomFieldDataType.Decimal => 1, CustomFieldDataType.Date => 2, CustomFieldDataType.Boolean => 3, _ => 4 };
        if (typed.Count(x => x) != 1 || !typed[typeIndex]) return $"{r.Label} has an invalid value type.";
        if (input.TextValue?.Length > r.MaxLength) return $"{r.Label} allows at most {r.MaxLength} characters.";
        if (input.DecimalValue is decimal n && (n <= -100000000000000m || n >= 100000000000000m || Math.Round(n, r.DecimalPlaces) != n || n < r.Minimum || n > r.Maximum)) return $"Check {r.Label}'s range and {r.DecimalPlaces}-decimal precision.";
        if (input.OptionId.HasValue && !d.Options.Any(x => x.Id == input.OptionId && (x.IsActive || old?.OptionId == input.OptionId))) return $"Select an active choice for {r.Label}.";
        return null;
    }
}
