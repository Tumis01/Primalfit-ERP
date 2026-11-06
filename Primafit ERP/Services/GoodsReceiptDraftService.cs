using System.Data;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed record ReceiptSourceCandidate(Guid DocumentId, string DocumentNumber, Guid LineId, Guid? OrderLineId,
    Guid ItemId, string ItemName, Guid? UomId, string UomName, decimal Factor,
    decimal Required, decimal Received, decimal Reserved, decimal Remaining, Guid? ClearingAccountId, Guid? DimensionSetId = null);
public sealed record ReceiptDraftLineInput(Guid SourceLineId, decimal QuantityBase, Guid? UomId, string UomName, decimal Factor);

public sealed class GoodsReceiptDraftService(IDbContextFactory<AppDbContext> factory)
{
    public async Task<List<ReceiptSourceCandidate>> GetCandidatesAsync(Guid companyId, Guid vendorId, bool invoiceSource, Guid? excludedDraftId = null, Guid? excludedReceiptId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (invoiceSource)
            return (await PurchaseInvoiceReceiptSources.GetAsync(db, companyId, vendorId, excludedDraftId, excludedReceiptId))
                .Select(x => new ReceiptSourceCandidate(x.InvoiceId, x.InvoiceNumber, x.LineId, x.OrderLineId, x.ItemId, x.ItemName,
                    x.UomId, x.UomName, x.Factor, x.Invoiced, x.Received, x.Reserved, x.Remaining, x.ClearingAccountId, x.DimensionSetId)).ToList();
        var orders = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == companyId && x.VendorId == vendorId && (x.Status == PurchaseOrderStatus.Open || x.Status == PurchaseOrderStatus.PartiallyReceived
                || (excludedReceiptId.HasValue && x.Lines.Any(l => db.GoodsReceiptLines.Any(r => r.GoodsReceiptId == excludedReceiptId && r.PurchaseOrderLineId == l.Id))))).ToListAsync();
        var ids = orders.SelectMany(x => x.Lines).Select(x => x.Id).ToList();
        var received = await db.GoodsReceiptLines.AsNoTracking().Where(x => x.GoodsReceiptId != excludedReceiptId && x.PurchaseOrderLineId.HasValue && ids.Contains(x.PurchaseOrderLineId.Value))
            .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(l => l.QuantityReceived) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
        var reserved = await db.GoodsReceiptDraftLines.AsNoTracking().Where(x => x.GoodsReceiptDraftId != excludedDraftId && x.PurchaseOrderLineId.HasValue && ids.Contains(x.PurchaseOrderLineId.Value))
            .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(l => l.QuantityReceived) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
        var adjustments = await InvoiceCorrectionBalances.PurchaseOrderAdjustmentsAsync(db, ids);
        var itemIds = orders.SelectMany(x => x.Lines).Select(x => x.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(x => x.CompanyId == companyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var clearing = await (from line in db.GoodsReceiptLines.AsNoTracking()
            join receipt in db.GoodsReceipts.AsNoTracking() on line.GoodsReceiptId equals receipt.Id
            join source in db.PurchaseOrderLines.AsNoTracking() on line.PurchaseOrderLineId equals source.Id
            where receipt.CompanyId == companyId && ids.Contains(source.Id)
            select new { source.PurchaseOrderId, receipt.InventoryGlAccountId }).ToListAsync();
        var result = new List<ReceiptSourceCandidate>();
        foreach (var order in orders)
        {
            var accounts = clearing.Where(x => x.PurchaseOrderId == order.Id).Select(x => x.InventoryGlAccountId).Distinct().ToList();
            if (accounts.Count > 1) continue;
            foreach (var line in order.Lines)
            {
                var remaining = Math.Max(0, line.QuantityOrdered - received.GetValueOrDefault(line.Id) - reserved.GetValueOrDefault(line.Id) - adjustments.GetValueOrDefault(line.Id));
                if (remaining <= UomConversion.QuantityTolerance || !items.TryGetValue(line.ItemId, out var item)) continue;
                result.Add(new(order.Id, order.OrderNumber, line.Id, line.Id, item.Id, item.Name, line.UomId, line.UomName,
                    line.UomConversionFactor, line.QuantityOrdered, received.GetValueOrDefault(line.Id), reserved.GetValueOrDefault(line.Id), remaining,
                    accounts.Count == 1 ? accounts[0] : null, line.DimensionSetId));
            }
        }
        return result.OrderBy(x => x.DocumentNumber).ThenBy(x => x.ItemName).ToList();
    }

    public async Task<string> CreateAsync(Guid companyId, Guid vendorId, Guid warehouseId, bool invoiceSource,
        IReadOnlyCollection<ReceiptDraftLineInput> lines, DateTime date, IDocumentNumberingService numbering)
    {
        if (lines.Count == 0 || lines.Any(x => x.QuantityBase <= 0) || lines.Select(x => x.SourceLineId).Distinct().Count() != lines.Count)
            return "Select distinct source lines and enter a positive quantity for each selected line.";
        var candidates = (await GetCandidatesAsync(companyId, vendorId, invoiceSource)).ToDictionary(x => x.LineId);
        if (lines.Any(x => !candidates.ContainsKey(x.SourceLineId) || UomConversion.Exceeds(x.QuantityBase, candidates[x.SourceLineId].Remaining)))
            return "A selected source line is no longer available in that quantity. Refresh the available lines.";
        var accounts = lines.Select(x => candidates[x.SourceLineId].ClearingAccountId).Where(x => x.HasValue).Distinct().ToList();
        if (accounts.Count > 1) return "These source documents use different GR/IR clearing accounts and cannot be combined on one receipt.";
        var receipt = new GoodsReceipt { CompanyId = companyId, WarehouseId = warehouseId, DateReceived = date,
            GrnNumber = await numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AP_GoodsReceipt, date),
            InventoryGlAccountId = accounts.SingleOrDefault() ?? Guid.Empty,
            Lines = lines.Select(x => new GoodsReceiptLine { DimensionSetId = candidates[x.SourceLineId].DimensionSetId, PurchaseOrderLineId = candidates[x.SourceLineId].OrderLineId,
                PurchaseInvoiceLineId = invoiceSource ? x.SourceLineId : null, QuantityReceived = x.QuantityBase,
                UomId = x.UomId, UomName = x.UomName, UomConversionFactor = x.Factor }).ToList() };
        return await SaveAsync(receipt, vendorId, warehouseId);
    }

    public async Task<(GoodsReceipt? Receipt, Guid VendorId)> GetAsync(Guid companyId, Guid draftId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var draft = await db.GoodsReceiptDrafts.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draftId);
        return draft == null ? (null, Guid.Empty) : (ToReceipt(draft), draft.VendorId);
    }

    public static GoodsReceipt ToReceipt(GoodsReceiptDraft draft) => new()
    {
        Id = draft.Id, CompanyId = draft.CompanyId, WarehouseId = draft.WarehouseId,
        GrnNumber = draft.GrnNumber, DateReceived = draft.DateReceived, ProjectId = draft.ProjectId,
        InventoryGlAccountId = draft.InventoryGlAccountId, CustomTransactionTypeId = draft.CustomTransactionTypeId,
        OverrideInventoryAssetGlAccountId = draft.OverrideInventoryAssetGlAccountId,
        OverrideGrIrClearingGlAccountId = draft.OverrideGrIrClearingGlAccountId,
        Lines = draft.Lines.Select(x => new GoodsReceiptLine { DimensionSetId = x.DimensionSetId,  Id = x.Id, GoodsReceiptId = draft.Id,
            PurchaseOrderLineId = x.PurchaseOrderLineId, PurchaseInvoiceLineId = x.PurchaseInvoiceLineId, QuantityReceived = x.QuantityReceived,
            UomId = x.UomId, UomName = x.UomName, UomConversionFactor = x.UomConversionFactor }).ToList()
    };

    public async Task<string> SaveAsync(GoodsReceipt receipt, Guid vendorId, Guid warehouseId)
    {
        if (receipt.DateReceived == default) return "Enter a valid receipt date.";
        if (receipt.Lines.Any(x => x.PurchaseInvoiceLineId.HasValue)) return await SaveInvoiceDraftAsync(receipt, vendorId, warehouseId);
        if (receipt.Id == Guid.Empty || receipt.CompanyId == Guid.Empty || vendorId == Guid.Empty || warehouseId == Guid.Empty)
            return "Select a supplier and warehouse.";
        var lines = receipt.Lines.Where(x => x.QuantityReceived > 0).ToList();
        if (receipt.Lines.Any(x => x.QuantityReceived < 0) || lines.Count == 0 || lines.Any(x => !x.PurchaseOrderLineId.HasValue)
            || lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != lines.Count)
            return "Select distinct purchase order lines and enter a positive quantity.";
        if (string.IsNullOrWhiteSpace(receipt.GrnNumber)) return "A receipt number is required.";
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId && x.CompanyId == receipt.CompanyId && (x.Type == WarehouseType.Purchase || x.Type == WarehouseType.Both)))
                return "The selected warehouse is not available for purchases.";
            var ids = lines.Select(x => x.PurchaseOrderLineId!.Value).ToList();
            var source = await db.PurchaseOrderLines.FromSqlRaw("SELECT * FROM [PurchaseOrderLines] WITH (UPDLOCK, HOLDLOCK)")
                .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (source.Count != ids.Count) return "A selected purchase order line no longer exists.";
            var orderIds = source.Values.Select(x => x.PurchaseOrderId).Distinct().ToList();
            var orders = await db.PurchaseOrders.Where(x => orderIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (orders.Count != orderIds.Count || orders.Values.Any(x => x.CompanyId != receipt.CompanyId || x.VendorId != vendorId || (x.Status != PurchaseOrderStatus.Open && x.Status != PurchaseOrderStatus.PartiallyReceived)))
                return "All source orders must be open and belong to the selected supplier and company.";
            var priorAccounts = await (from oldLine in db.GoodsReceiptLines.AsNoTracking()
                join oldReceipt in db.GoodsReceipts.AsNoTracking() on oldLine.GoodsReceiptId equals oldReceipt.Id
                join poLine in db.PurchaseOrderLines.AsNoTracking() on oldLine.PurchaseOrderLineId equals poLine.Id
                where oldReceipt.CompanyId == receipt.CompanyId && orderIds.Contains(poLine.PurchaseOrderId)
                select oldReceipt.InventoryGlAccountId).Distinct().ToListAsync();
            if (priorAccounts.Count > 1) return "These source documents use different GR/IR clearing accounts and cannot be combined on one receipt.";
            if (priorAccounts.Count == 1) {
                if (receipt.OverrideGrIrClearingGlAccountId.HasValue && receipt.OverrideGrIrClearingGlAccountId != priorAccounts[0]) return "The clearing account is fixed by the previous receipt for this purchase order.";
                receipt.InventoryGlAccountId = priorAccounts[0];
            }
            var routeError = await InvoiceRoutePolicy.ValidateAsync(db, receipt.CompanyId, TransactionTemplateModule.AP,
                new InvoiceRoute { TemplateId = receipt.CustomTransactionTypeId, DebitAccountId = receipt.OverrideInventoryAssetGlAccountId, CreditAccountId = receipt.OverrideGrIrClearingGlAccountId });
            if (routeError != null) return routeError;
            if (await db.GoodsReceipts.AnyAsync(x => x.CompanyId == receipt.CompanyId && (x.Id == receipt.Id || x.GrnNumber == receipt.GrnNumber)))
                return "This receipt has already been submitted or its number is in use.";
            if (await db.GoodsReceiptDrafts.AnyAsync(x => x.CompanyId == receipt.CompanyId && x.Id != receipt.Id && x.GrnNumber == receipt.GrnNumber))
                return "This receipt number is already in use by another draft.";
            var received = await db.GoodsReceiptLines.Where(x => x.PurchaseOrderLineId.HasValue && ids.Contains(x.PurchaseOrderLineId.Value))
                .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(l => l.QuantityReceived) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
            var reserved = await db.GoodsReceiptDraftLines.Where(x => x.PurchaseOrderLineId.HasValue && ids.Contains(x.PurchaseOrderLineId.Value) && x.GoodsReceiptDraftId != receipt.Id)
                .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(l => l.QuantityReceived) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
            var adjusted = await InvoiceCorrectionBalances.PurchaseOrderAdjustmentsAsync(db, ids);
            var itemIds = source.Values.Select(x => x.ItemId).Distinct().ToList();
            var items = await db.Items.Include(x => x.UomConversions).Where(x => x.CompanyId == receipt.CompanyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (items.Count != itemIds.Count) return "A source item is unavailable in this company.";
            var uoms = await db.UnitOfMeasures.Where(x => x.CompanyId == receipt.CompanyId).Select(x => x.Id).ToHashSetAsync();
            var rules = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == receipt.CompanyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var factors = await db.ItemUomConversionLines.AsNoTracking().Where(x => x.IsActive && itemIds.Contains(x.ItemId))
                .GroupBy(x => new { x.ItemId, x.UomId }).ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
            foreach (var line in lines)
            {
                var poLine = source[line.PurchaseOrderLineId!.Value]; var item = items[poLine.ItemId];
                var available = Math.Max(0m, poLine.QuantityOrdered - received.GetValueOrDefault(poLine.Id) - adjusted.GetValueOrDefault(poLine.Id) - reserved.GetValueOrDefault(poLine.Id));
                if (UomConversion.Exceeds(line.QuantityReceived, available)) return $"{item.Name}: only {available:G29} base units remain available after other drafts.";
                line.QuantityReceived = UomConversion.WithinRemaining(line.QuantityReceived, available);
                var itemFactors = factors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                if (line.UomId.HasValue && (!uoms.Contains(line.UomId.Value) || (line.UomId != item.UomId && !itemFactors.ContainsKey(line.UomId.Value)
                    && !UomConversion.IsConfigured(item, line.UomId))))
                    return $"The selected UOM for {item.Name} is not configured.";
                var expected = UomConversion.FactorFor(item, line.UomId, itemFactors, rules);
                if (line.UomConversionFactor <= 0 || Math.Abs(line.UomConversionFactor - expected) > 0.000000000000001m)
                    return $"The UOM conversion for {item.Name} has changed. Refresh the form.";
            }
            var draft = await db.GoodsReceiptDrafts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == receipt.Id);
            if (draft != null && draft.CompanyId != receipt.CompanyId) return "Receipt draft not found.";
            if (draft == null) { draft = new GoodsReceiptDraft { Id = receipt.Id, CompanyId = receipt.CompanyId }; db.GoodsReceiptDrafts.Add(draft); }
            else { db.GoodsReceiptDraftLines.RemoveRange(draft.Lines); await db.SaveChangesAsync(); draft.Lines.Clear(); }
            draft.VendorId = vendorId; draft.WarehouseId = warehouseId; draft.GrnNumber = receipt.GrnNumber;
            draft.DateReceived = receipt.DateReceived; draft.ProjectId = receipt.ProjectId; draft.InventoryGlAccountId = receipt.InventoryGlAccountId;
            draft.CustomTransactionTypeId = receipt.CustomTransactionTypeId; draft.OverrideInventoryAssetGlAccountId = receipt.OverrideInventoryAssetGlAccountId;
            draft.OverrideGrIrClearingGlAccountId = receipt.OverrideGrIrClearingGlAccountId;
            draft.Lines = lines.Select(x => new GoodsReceiptDraftLine { DimensionSetId = x.DimensionSetId,  Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id, GoodsReceiptDraftId = draft.Id,
                PurchaseOrderLineId = x.PurchaseOrderLineId!.Value, QuantityReceived = x.QuantityReceived, UomId = x.UomId,
                UomName = x.UomName, UomConversionFactor = x.UomConversionFactor }).ToList();
            db.GoodsReceiptDraftLines.AddRange(draft.Lines);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (DbUpdateException) { return "The receipt draft could not be saved. Refresh the available lines and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The receipt draft could not be saved because the database was busy or unavailable. Try again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "The receipt draft could not be saved because another transaction is using the same lines. Refresh and try again."; }
    }

    private async Task<string> SaveInvoiceDraftAsync(GoodsReceipt receipt, Guid vendorId, Guid warehouseId)
    {
        var lines = receipt.Lines.Where(x => x.QuantityReceived > 0).ToList();
        if (receipt.Id == Guid.Empty || receipt.CompanyId == Guid.Empty || vendorId == Guid.Empty || warehouseId == Guid.Empty) return "Select a supplier and warehouse.";
        if (receipt.Lines.Any(x => x.QuantityReceived < 0) || lines.Count == 0 || lines.Any(x => !x.PurchaseInvoiceLineId.HasValue)
            || lines.Select(x => x.PurchaseInvoiceLineId).Distinct().Count() != lines.Count) return "Select distinct invoice lines and enter a positive quantity.";
        if (string.IsNullOrWhiteSpace(receipt.GrnNumber)) return "A receipt number is required.";
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId && x.CompanyId == receipt.CompanyId && (x.Type == WarehouseType.Purchase || x.Type == WarehouseType.Both))) return "The selected warehouse is not available for purchases.";
            var ids = lines.Select(x => x.PurchaseInvoiceLineId!.Value).ToList();
            await db.PurchaseInvoiceLines.FromSqlRaw("SELECT * FROM [PurchaseInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var source = await db.PurchaseInvoiceLines.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var roots = source.Values.Where(x => x.PurchaseOrderLineId.HasValue).Select(x => x.PurchaseOrderLineId!.Value).ToList();
            if (roots.Distinct().Count() != roots.Count) return "Select at most one invoice line for each purchase order line.";
            await db.PurchaseOrderLines.FromSqlRaw("SELECT * FROM [PurchaseOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => roots.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var candidates = (await PurchaseInvoiceReceiptSources.GetAsync(db, receipt.CompanyId, vendorId, receipt.Id)).ToDictionary(x => x.LineId);
            if (lines.Any(x => !candidates.ContainsKey(x.PurchaseInvoiceLineId!.Value))) return "A selected invoice line is no longer available. Refresh the form.";
            var clearingAccounts = lines.Select(x => candidates[x.PurchaseInvoiceLineId!.Value].ClearingAccountId).Distinct().ToList();
            if (clearingAccounts.Count != 1 || !clearingAccounts[0].HasValue) return "All selected invoice lines must use the same recorded clearing account.";
            if (receipt.OverrideGrIrClearingGlAccountId.HasValue && receipt.OverrideGrIrClearingGlAccountId != clearingAccounts[0]) return "The clearing account is fixed by the posted invoice.";
            receipt.InventoryGlAccountId = clearingAccounts[0]!.Value;
            var routeError = await InvoiceRoutePolicy.ValidateAsync(db, receipt.CompanyId, TransactionTemplateModule.AP,
                new InvoiceRoute { TemplateId = receipt.CustomTransactionTypeId, DebitAccountId = receipt.OverrideInventoryAssetGlAccountId, CreditAccountId = receipt.OverrideGrIrClearingGlAccountId });
            if (routeError != null) return routeError;
            if (await db.GoodsReceipts.AnyAsync(x => x.CompanyId == receipt.CompanyId && (x.Id == receipt.Id || x.GrnNumber == receipt.GrnNumber))
                || await db.GoodsReceiptDrafts.AnyAsync(x => x.CompanyId == receipt.CompanyId && x.Id != receipt.Id && x.GrnNumber == receipt.GrnNumber)) return "This receipt number is already in use.";
            var itemIds = candidates.Values.Select(x => x.ItemId).Distinct().ToList();
            var items = await db.Items.Include(x => x.UomConversions).Where(x => x.CompanyId == receipt.CompanyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var uoms = await db.UnitOfMeasures.Where(x => x.CompanyId == receipt.CompanyId).Select(x => x.Id).ToHashSetAsync();
            var rules = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == receipt.CompanyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var factors = await db.ItemUomConversionLines.AsNoTracking().Where(x => x.IsActive && itemIds.Contains(x.ItemId)).GroupBy(x => new { x.ItemId, x.UomId }).ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
            foreach (var line in lines)
            {
                var candidate = candidates[line.PurchaseInvoiceLineId!.Value]; var item = items[candidate.ItemId];
                if (line.PurchaseOrderLineId != candidate.OrderLineId || UomConversion.Exceeds(line.QuantityReceived, candidate.Remaining)) return $"{item.Name}: the source or available quantity has changed. Refresh the form.";
                line.QuantityReceived = UomConversion.WithinRemaining(line.QuantityReceived, candidate.Remaining);
                var itemFactors = factors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                if (line.UomId.HasValue && (!uoms.Contains(line.UomId.Value) || (line.UomId != item.UomId && !itemFactors.ContainsKey(line.UomId.Value) && !UomConversion.IsConfigured(item, line.UomId)))) return $"The selected UOM for {item.Name} is not configured.";
                if (line.UomConversionFactor <= 0 || Math.Abs(line.UomConversionFactor - UomConversion.FactorFor(item, line.UomId, itemFactors, rules)) > 0.000000000000001m) return $"The UOM conversion for {item.Name} has changed. Refresh the form.";
            }
            var draft = await db.GoodsReceiptDrafts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == receipt.Id);
            if (draft != null && draft.CompanyId != receipt.CompanyId) return "Receipt draft not found.";
            if (draft == null) { draft = new GoodsReceiptDraft { Id = receipt.Id, CompanyId = receipt.CompanyId }; db.GoodsReceiptDrafts.Add(draft); }
            else { db.GoodsReceiptDraftLines.RemoveRange(draft.Lines); await db.SaveChangesAsync(); draft.Lines.Clear(); }
            draft.VendorId = vendorId; draft.WarehouseId = warehouseId; draft.GrnNumber = receipt.GrnNumber; draft.DateReceived = receipt.DateReceived;
            draft.ProjectId = receipt.ProjectId; draft.InventoryGlAccountId = receipt.InventoryGlAccountId; draft.CustomTransactionTypeId = receipt.CustomTransactionTypeId;
            draft.OverrideInventoryAssetGlAccountId = receipt.OverrideInventoryAssetGlAccountId; draft.OverrideGrIrClearingGlAccountId = receipt.OverrideGrIrClearingGlAccountId;
            draft.Lines = lines.Select(x => new GoodsReceiptDraftLine { DimensionSetId = x.DimensionSetId,  Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id, GoodsReceiptDraftId = draft.Id,
                PurchaseOrderLineId = x.PurchaseOrderLineId, PurchaseInvoiceLineId = x.PurchaseInvoiceLineId, QuantityReceived = x.QuantityReceived,
                UomId = x.UomId, UomName = x.UomName, UomConversionFactor = x.UomConversionFactor }).ToList();
            db.GoodsReceiptDraftLines.AddRange(draft.Lines);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (DbUpdateException) { return "The receipt draft could not be saved. Refresh the available lines and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The receipt draft could not be saved because the database is busy. Try again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "Another transaction is using these lines. Refresh and try again."; }
    }

    public async Task<string> DeleteAsync(Guid companyId, Guid draftId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var draft = await db.GoodsReceiptDrafts.FirstOrDefaultAsync(x => x.Id == draftId && x.CompanyId == companyId);
        if (draft == null) return "Receipt draft not found.";
        db.GoodsReceiptDrafts.Remove(draft); await db.SaveChangesAsync(); return string.Empty;
    }

    private static bool HasTransientSqlError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 1205 || sql.Number == 1222 || sql.Number == -2)) return true;
        return false;
    }
}
