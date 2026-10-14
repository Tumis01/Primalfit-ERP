using System.Data;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public interface IDocumentNumberingService
{
    Task<string> PeekNextNumberAsync(Guid sequenceDefinitionId, DateTime? referenceDate = null, Guid? exceptEntityId = null);
    Task<string> GenerateAndReserveNextNumberAsync(Guid sequenceDefinitionId, DateTime? referenceDate = null);
    Task<string> ReserveNextNumberWithinTransactionAsync(AppDbContext db, Guid sequenceDefinitionId, DateTime? referenceDate = null, Guid? exceptEntityId = null);
    Task<string> PeekNextNumberAsync(Guid companyId, SequenceTargetType targetType, DateTime? referenceDate = null);
    Task<string> GenerateAndReserveNextNumberAsync(Guid companyId, SequenceTargetType targetType, DateTime? referenceDate = null);
    Task<SequenceDefinition> GetActiveDefinitionAsync(Guid companyId, SequenceTargetType targetType);
    Task<List<SequenceDefinition>> GetDefinitionsAsync(Guid companyId, SequenceTargetType? targetType = null);
    Task<string?> SaveDefinitionAsync(SequenceDefinition definition);
    Task<string?> ActivateDefinitionAsync(Guid definitionId, Guid companyId);
    Task<string?> DeleteDefinitionAsync(Guid definitionId, Guid companyId);
    Task<bool> IsCodeAvailableAsync(Guid companyId, SequenceTargetType targetType, string code, Guid? exceptId = null);
    Task<Dictionary<Guid, List<string>>> GetSequenceAssignmentsAsync(Guid companyId, SequenceTargetType targetType);
}

public sealed class DocumentNumberingService : IDocumentNumberingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DocumentNumberingService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<string> PeekNextNumberAsync(Guid sequenceDefinitionId, DateTime? referenceDate = null, Guid? exceptEntityId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var definition = await db.SequenceDefinitions.AsNoTracking()
            .Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.Id == sequenceDefinitionId);
        if (definition is null) throw new InvalidOperationException("Sequence definition was not found.");
        var components = ValidateAndNormalize(definition);
        var date = referenceDate ?? DateTime.Today;
        var scope = await ScopePeriodKeyAsync(db, definition, date);
        var tracker = await db.SequenceTrackers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SequenceDefinitionId == definition.Id && x.ScopePeriodKey == scope);
        var number = (tracker?.LastNumber ?? 0) + 1;
        while (await IsNumberInUseAsync(db, definition, FormatNumber(components, number, date, await GetFiscalYearAsync(db, definition.CompanyId, date)), exceptEntityId))
            number++;
        EnsureFits(components, number);
        return FormatNumber(components, number, date, await GetFiscalYearAsync(db, definition.CompanyId, date));
    }

    public async Task<string> GenerateAndReserveNextNumberAsync(Guid sequenceDefinitionId, DateTime? referenceDate = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var definition = await db.SequenceDefinitions.Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.Id == sequenceDefinitionId);
        if (definition is null) throw new InvalidOperationException("Sequence definition was not found.");

        var components = ValidateAndNormalize(definition);
        var date = referenceDate ?? DateTime.Today;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var scope = await ScopePeriodKeyAsync(db, definition, date);

        // The unique period index plus SERIALIZABLE protects both an existing tracker
        // row and the empty-key range while concurrent callers allocate the first number.
        var tracker = await db.SequenceTrackers
            .FirstOrDefaultAsync(x => x.SequenceDefinitionId == definition.Id && x.ScopePeriodKey == scope);
        if (tracker is null)
        {
            tracker = new SequenceTracker { SequenceDefinitionId = definition.Id, ScopePeriodKey = scope };
            db.SequenceTrackers.Add(tracker);
        }

        var fiscalYear = await GetFiscalYearAsync(db, definition.CompanyId, date);
        var number = tracker.LastNumber + 1;
        string formatted;
        do
        {
            EnsureFits(components, number);
            formatted = FormatNumber(components, number, date, fiscalYear);
            if (!await IsNumberInUseAsync(db, definition, formatted)) break;
            number++;
        } while (true);

        tracker.LastNumber = number;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return formatted;
    }

    public async Task<string> ReserveNextNumberWithinTransactionAsync(
        AppDbContext db,
        Guid sequenceDefinitionId,
        DateTime? referenceDate = null,
        Guid? exceptEntityId = null)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A database transaction is required to reserve a number with its entity.");

        var definition = await db.SequenceDefinitions.Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.Id == sequenceDefinitionId);
        if (definition is null || !IsCodeTarget(definition.TargetType))
            throw new InvalidOperationException("The selected item code sequence was not found.");

        var components = ValidateAndNormalize(definition);
        var date = referenceDate ?? DateTime.Today;
        var scope = await ScopePeriodKeyAsync(db, definition, date);
        var tracker = await db.SequenceTrackers
            .FirstOrDefaultAsync(x => x.SequenceDefinitionId == definition.Id && x.ScopePeriodKey == scope);
        if (tracker is null)
        {
            tracker = new SequenceTracker { SequenceDefinitionId = definition.Id, ScopePeriodKey = scope };
            db.SequenceTrackers.Add(tracker);
        }

        var fiscalYear = await GetFiscalYearAsync(db, definition.CompanyId, date);
        var number = tracker.LastNumber + 1;
        string formatted;
        do
        {
            EnsureFits(components, number);
            formatted = FormatNumber(components, number, date, fiscalYear);
            if (!await IsNumberInUseAsync(db, definition, formatted, exceptEntityId)) break;
            number++;
        } while (true);

        tracker.LastNumber = number;
        return formatted;
    }

    public async Task<string> PeekNextNumberAsync(Guid companyId, SequenceTargetType targetType, DateTime? referenceDate = null)
    {
        var definition = await GetActiveDefinitionAsync(companyId, targetType);
        return await PeekNextNumberAsync(definition.Id, referenceDate);
    }

    public async Task<string> GenerateAndReserveNextNumberAsync(Guid companyId, SequenceTargetType targetType, DateTime? referenceDate = null)
    {
        var definition = await GetActiveDefinitionAsync(companyId, targetType);
        return await GenerateAndReserveNextNumberAsync(definition.Id, referenceDate);
    }

    public async Task<List<SequenceDefinition>> GetDefinitionsAsync(Guid companyId, SequenceTargetType? targetType = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.SequenceDefinitions.AsNoTracking().Include(x => x.Components).Where(x => x.CompanyId == companyId);
        if (targetType.HasValue) query = query.Where(x => x.TargetType == targetType.Value);
        return await query.OrderBy(x => x.TargetType).ThenByDescending(x => x.IsSystemDefault).ThenBy(x => x.Name).ToListAsync();
    }

    public async Task<SequenceDefinition> GetActiveDefinitionAsync(Guid companyId, SequenceTargetType targetType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var current = await db.SequenceDefinitions.AsNoTracking().Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.TargetType == targetType && x.IsActive);
        if (current is not null) return current;

        var definition = await db.SequenceDefinitions.Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.TargetType == targetType && x.IsSystemDefault);
        if (definition is null)
        {
            definition = CreateDefault(companyId, targetType);
            db.SequenceDefinitions.Add(definition);
        }
        else
        {
            definition.IsActive = true;
        }
        await db.SaveChangesAsync();
        return definition;
    }

    public async Task<string?> SaveDefinitionAsync(SequenceDefinition definition)
    {
        if (definition.CompanyId == Guid.Empty) return "A company is required.";
        if (string.IsNullOrWhiteSpace(definition.Name)) return "Sequence name is required.";
        var normalized = ValidateAndNormalize(definition, out var error);
        if (error is not null) return error;

        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.SequenceDefinitions.AnyAsync(x => x.CompanyId == definition.CompanyId
            && x.TargetType == definition.TargetType && x.Name == definition.Name.Trim() && x.Id != definition.Id))
            return "A sequence with this name already exists for the selected target.";
        var duplicate = await db.SequenceDefinitions.AsNoTracking()
            .Include(x => x.Components)
            .Where(x => x.CompanyId == definition.CompanyId && x.TargetType == definition.TargetType && x.Id != definition.Id)
            .ToListAsync();
        var structure = StructureKey(normalized);
        if (duplicate.Any(x => StructureKey(x.Components.OrderBy(c => c.OrderIndex).ToList()) == structure))
            return "An identical component structure already exists for this target type.";

        var existing = definition.Id == Guid.Empty ? null : await db.SequenceDefinitions
            .Include(x => x.Components).FirstOrDefaultAsync(x => x.Id == definition.Id && x.CompanyId == definition.CompanyId);
        if (definition.IsActive)
        {
            var active = await db.SequenceDefinitions.FirstOrDefaultAsync(x => x.CompanyId == definition.CompanyId && x.TargetType == definition.TargetType && x.IsActive && x.Id != definition.Id);
            if (active is not null)
            {
                active.IsActive = false;
                await db.SaveChangesAsync();
            }
        }

        if (existing is null)
        {
            definition.Id = Guid.NewGuid();
            foreach (var component in normalized)
            {
                component.Id = Guid.NewGuid();
                component.SequenceDefinitionId = definition.Id;
            }
            definition.Components = normalized;
            db.SequenceDefinitions.Add(definition);
        }
        else
        {
            existing.Name = definition.Name.Trim();
            existing.Description = definition.Description?.Trim();
            existing.TargetType = definition.TargetType;
            existing.ResetInterval = definition.ResetInterval;
            existing.IsSystemDefault = definition.IsSystemDefault;
            existing.IsActive = definition.IsActive;
            db.SequenceComponents.RemoveRange(existing.Components);
            foreach (var component in normalized)
            {
                component.Id = Guid.NewGuid();
                component.SequenceDefinitionId = existing.Id;
            }
            existing.Components = normalized;
        }

        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> ActivateDefinitionAsync(Guid definitionId, Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var definition = await db.SequenceDefinitions
            .Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.Id == definitionId && x.CompanyId == companyId);
        if (definition is null) return "Sequence definition was not found.";

        var validationError = ValidateDefinition(definition);
        if (validationError is not null) return validationError;
        if (definition.IsActive)
        {
            await transaction.CommitAsync();
            return null;
        }

        var currentlyActive = await db.SequenceDefinitions
            .Where(x => x.CompanyId == companyId && x.TargetType == definition.TargetType && x.IsActive && x.Id != definitionId)
            .ToListAsync();
        foreach (var active in currentlyActive) active.IsActive = false;
        if (currentlyActive.Count > 0) await db.SaveChangesAsync();

        definition.IsActive = true;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return null;
    }

    public async Task<bool> IsCodeAvailableAsync(Guid companyId, SequenceTargetType targetType, string code, Guid? exceptId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return targetType switch
        {
            SequenceTargetType.CustomerCode => !await db.Customers.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == code.ToLower() && (!exceptId.HasValue || x.Id != exceptId.Value)),
            SequenceTargetType.VendorCode => !await db.Vendors.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == code.ToLower() && (!exceptId.HasValue || x.Id != exceptId.Value)),
            SequenceTargetType.ItemCategoryCode => !await db.ItemCategories.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == code.ToLower() && (!exceptId.HasValue || x.Id != exceptId.Value)),
            SequenceTargetType.ItemCode => !await db.Items.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == code.ToLower() && (!exceptId.HasValue || x.Id != exceptId.Value)),
            _ => true
        };
    }

    public async Task<Dictionary<Guid, List<string>>> GetSequenceAssignmentsAsync(Guid companyId, SequenceTargetType targetType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return targetType switch
        {
            SequenceTargetType.CustomerCode => await db.CustomerGroups.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.SequenceDefinitionId.HasValue)
                .GroupBy(x => x.SequenceDefinitionId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Name).OrderBy(x => x).ToList()),
            SequenceTargetType.VendorCode => await db.VendorGroups.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.SequenceDefinitionId.HasValue)
                .GroupBy(x => x.SequenceDefinitionId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Name).OrderBy(x => x).ToList()),
            SequenceTargetType.ItemCode => await db.ItemCategories.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ItemCodeSequenceDefinitionId.HasValue)
                .GroupBy(x => x.ItemCodeSequenceDefinitionId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Name).OrderBy(x => x).ToList()),
            _ => new Dictionary<Guid, List<string>>()
        };
    }

    public async Task<string?> DeleteDefinitionAsync(Guid definitionId, Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var definition = await db.SequenceDefinitions.Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.Id == definitionId && x.CompanyId == companyId);
        if (definition is null) return "Sequence definition was not found.";
        if (definition.IsSystemDefault) return "The system default sequence cannot be deleted.";
        if (await db.CustomerGroups.AnyAsync(x => x.SequenceDefinitionId == definitionId)
            || await db.VendorGroups.AnyAsync(x => x.SequenceDefinitionId == definitionId)
            || await db.ItemCategories.AnyAsync(x => x.ItemCodeSequenceDefinitionId == definitionId))
            return "This sequence is assigned to a group or category. Remove those assignments before deleting it.";

        if (definition.IsActive)
        {
            var systemDefault = await db.SequenceDefinitions.FirstOrDefaultAsync(x => x.CompanyId == companyId
                && x.TargetType == definition.TargetType && x.IsSystemDefault);
            if (systemDefault is not null)
            {
                systemDefault.IsActive = true;
                definition.IsActive = false;
                await db.SaveChangesAsync();
            }
        }
        db.SequenceTrackers.RemoveRange(db.SequenceTrackers.Where(x => x.SequenceDefinitionId == definitionId));
        db.SequenceDefinitions.Remove(definition);
        await db.SaveChangesAsync();
        return null;
    }

    public static string? ValidateDefinition(SequenceDefinition definition)
    {
        ValidateAndNormalize(definition, out var error);
        return error;
    }

    public static SequenceDefinition CreateDefault(Guid companyId, SequenceTargetType targetType)
    {
        var prefix = targetType switch
        {
            SequenceTargetType.AR_Quote => "QUO-",
            SequenceTargetType.AR_Order => "ORD-",
            SequenceTargetType.AR_Invoice => "INV-",
            SequenceTargetType.AR_CreditNote => "CRN-",
            SequenceTargetType.AR_ReceiptRefund => "RFD-",
            SequenceTargetType.AR_Shipment => "SHP-",
            SequenceTargetType.AR_CustomerReceipt => "RCT-",
            SequenceTargetType.AR_Adjustment => "ARA-",
            SequenceTargetType.AP_Request => "REQ-",
            SequenceTargetType.AP_PurchaseOrder => "PO-",
            SequenceTargetType.AP_Invoice => "PINV-",
            SequenceTargetType.AP_DebitNote => "DBN-",
            SequenceTargetType.AP_ReturnToVendor => "VRT-",
            SequenceTargetType.AP_GoodsReceipt => "GRN-",
            SequenceTargetType.AP_VendorPayment => "VPM-",
            SequenceTargetType.AP_Adjustment => "APA-",
            SequenceTargetType.CustomerCode => "CUST-",
            SequenceTargetType.VendorCode => "VEND-",
            SequenceTargetType.ItemCategoryCode => "CAT-",
            SequenceTargetType.ItemCode => "ITEM-",
            _ => "DOC-"
        };
        var definition = new SequenceDefinition
        {
            CompanyId = companyId,
            TargetType = targetType,
            Name = $"System Default - {targetType}",
            Description = "Company system default sequence.",
            ResetInterval = IsCodeTarget(targetType) ? ResetInterval.Never : ResetInterval.Yearly,
            IsSystemDefault = true,
            IsActive = true
        };
        definition.Components.Add(new SequenceComponent
        {
            OrderIndex = 0,
            ComponentType = SequenceComponentType.Constant,
            Length = prefix.Length,
            Formula = prefix
        });
        definition.Components.Add(new SequenceComponent
        {
            OrderIndex = 1,
            ComponentType = SequenceComponentType.SequenceNumber,
            Length = 4,
            Formula = "0000"
        });
        return definition;
    }

    public static bool IsCodeTarget(SequenceTargetType type) => type is
        SequenceTargetType.CustomerCode or SequenceTargetType.VendorCode or SequenceTargetType.ItemCode;

    private static List<SequenceComponent> ValidateAndNormalize(SequenceDefinition definition)
        => ValidateAndNormalize(definition, out var error) is var result && error is null
            ? result
            : throw new InvalidOperationException(error ?? "Invalid sequence definition.");

    private static List<SequenceComponent> ValidateAndNormalize(SequenceDefinition definition, out string? error)
    {
        error = null;
        var components = definition.Components.OrderBy(x => x.OrderIndex).ToList();
        if (components.Count == 0) { error = "Add at least one component."; return components; }
        if (components.Count(x => x.ComponentType == SequenceComponentType.SequenceNumber) != 1)
        { error = "A sequence must contain exactly one Sequence Number component."; return components; }
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            component.OrderIndex = i;
            var validLength = component.ComponentType switch
            {
                SequenceComponentType.Constant or SequenceComponentType.SequenceNumber => component.Length is >= 1 and <= 20,
                SequenceComponentType.Year => component.Length is >= 2 and <= 4,
                SequenceComponentType.Month or SequenceComponentType.Day => component.Length == 2,
                SequenceComponentType.FiscalYear => component.Length is >= 1 and <= 3,
                _ => false
            };
            if (!validLength) { error = $"Length {component.Length} is not valid for {component.ComponentType}."; return components; }
            if (component.ComponentType == SequenceComponentType.Constant)
            {
                if (component.Formula is null || component.Formula.Length != component.Length)
                { error = $"Constant components need text matching their configured length ({component.Length})."; return components; }
            }
            else
            {
                component.Formula = component.ComponentType switch
                {
                    SequenceComponentType.SequenceNumber => new string('0', component.Length),
                    SequenceComponentType.Year => new string('Y', component.Length),
                    SequenceComponentType.Month => "MM",
                    SequenceComponentType.Day => "DD",
                    SequenceComponentType.FiscalYear => "FY",
                    _ => string.Empty
                };
            }
        }
        return components;
    }

    private static string StructureKey(IEnumerable<SequenceComponent> components) => string.Join("|", components
        .OrderBy(x => x.OrderIndex)
        .Select(x => $"{(int)x.ComponentType}:{x.Length}:{(x.ComponentType == SequenceComponentType.Constant ? x.Formula : string.Empty)}"));

    private static void EnsureFits(IReadOnlyCollection<SequenceComponent> components, long number)
    {
        var length = components.Single(x => x.ComponentType == SequenceComponentType.SequenceNumber).Length;
        if (number < 1 || number.ToString().Length > length)
            throw new InvalidOperationException($"The sequence exceeded its configured {length}-digit number component.");
    }

    private static string FormatNumber(IReadOnlyCollection<SequenceComponent> components, long number, DateTime date, int fiscalYear)
    {
        EnsureFits(components, number);
        return string.Concat(components.OrderBy(x => x.OrderIndex).Select(component => component.ComponentType switch
        {
            SequenceComponentType.Constant => component.Formula ?? string.Empty,
            SequenceComponentType.SequenceNumber => number.ToString().PadLeft(component.Length, '0'),
            SequenceComponentType.Year => (date.Year % (int)Math.Pow(10, component.Length)).ToString().PadLeft(component.Length, '0'),
            SequenceComponentType.Month => date.ToString("MM"),
            SequenceComponentType.Day => date.ToString("dd"),
            SequenceComponentType.FiscalYear => (fiscalYear % (int)Math.Pow(10, component.Length)).ToString().PadLeft(component.Length, '0'),
            _ => string.Empty
        }));
    }

    private static async Task<string> ScopePeriodKeyAsync(AppDbContext db, SequenceDefinition definition, DateTime date)
    {
        return definition.ResetInterval switch
        {
            ResetInterval.Never => string.Empty,
            ResetInterval.Yearly => date.ToString("yyyy"),
            ResetInterval.Monthly => date.ToString("yyyy-MM"),
            ResetInterval.Periodically => $"P{date.Year}-{(date.DayOfYear - 1) / 30 + 1:00}",
            ResetInterval.FiscalYearly => $"FY{await GetFiscalYearAsync(db, definition.CompanyId, date)}",
            _ => string.Empty
        };
    }

    private static async Task<int> GetFiscalYearAsync(AppDbContext db, Guid companyId, DateTime date)
    {
        var company = await db.CompanyDetails.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyDetailsId == companyId);
        if (company?.FiscalStartYear is not DateOnly start) return date.Year;
        var began = new DateTime(date.Year, start.Month, Math.Min(start.Day, DateTime.DaysInMonth(date.Year, start.Month)));
        return date >= began ? date.Year : date.Year - 1;
    }

    private static async Task<bool> IsNumberInUseAsync(AppDbContext db, SequenceDefinition definition, string number, Guid? exceptEntityId = null)
    {
        var companyId = definition.CompanyId;
        return definition.TargetType switch
        {
            SequenceTargetType.AR_Quote or SequenceTargetType.AR_Order or SequenceTargetType.AR_Invoice =>
                await db.SalesOrders.AnyAsync(x => x.CompanyId == companyId && x.OrderNumber == number),
            SequenceTargetType.AR_CreditNote => await db.CreditNotes.AnyAsync(x => x.CompanyId == companyId && x.CreditNoteNumber == number),
            SequenceTargetType.AR_ReceiptRefund => await db.ReceiptRefunds.AnyAsync(x => x.CompanyId == companyId && x.RefundNumber == number),
            SequenceTargetType.AR_Shipment => await db.SalesShipments.AnyAsync(x => x.CompanyId == companyId && x.ShipmentNumber == number),
            SequenceTargetType.AR_CustomerReceipt => await db.CustomerPayments.AnyAsync(x => x.CompanyId == companyId && x.Reference == number),
            SequenceTargetType.AR_Adjustment or SequenceTargetType.AP_Adjustment => await db.GLJournalHeaders.AnyAsync(x => x.CompanyId == companyId && x.JournalNumber == number),
            SequenceTargetType.AP_Request or SequenceTargetType.AP_PurchaseOrder or SequenceTargetType.AP_Invoice =>
                await db.PurchaseOrders.AnyAsync(x => x.CompanyId == companyId && x.OrderNumber == number),
            SequenceTargetType.AP_DebitNote => await db.DebitNotes.AnyAsync(x => x.CompanyId == companyId && x.DebitNoteNumber == number),
            SequenceTargetType.AP_ReturnToVendor => await db.VendorReturns.AnyAsync(x => x.CompanyId == companyId && x.ReturnNumber == number),
            SequenceTargetType.AP_GoodsReceipt => await db.GoodsReceipts.AnyAsync(x => x.CompanyId == companyId && x.GrnNumber == number),
            SequenceTargetType.AP_VendorPayment => await db.VendorPayments.AnyAsync(x => x.VendorBill != null && x.VendorBill.CompanyId == companyId && x.Reference == number),
            SequenceTargetType.CustomerCode => await db.Customers.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == number.ToLower() && (!exceptEntityId.HasValue || x.Id != exceptEntityId.Value)),
            SequenceTargetType.VendorCode => await db.Vendors.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == number.ToLower() && (!exceptEntityId.HasValue || x.Id != exceptEntityId.Value)),
            SequenceTargetType.ItemCategoryCode => await db.ItemCategories.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == number.ToLower() && (!exceptEntityId.HasValue || x.Id != exceptEntityId.Value)),
            SequenceTargetType.ItemCode => await db.Items.AnyAsync(x => x.CompanyId == companyId && x.Code.ToLower() == number.ToLower() && (!exceptEntityId.HasValue || x.Id != exceptEntityId.Value)),
            _ => false
        };
    }
}
