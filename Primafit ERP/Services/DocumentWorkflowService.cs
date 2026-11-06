using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public sealed record WorkflowLineOption(Guid Id, Guid DocumentId, Guid PartnerId, Guid? SiteId, Guid CurrencyId, decimal ExchangeRate, string DocumentNumber, string BusinessPartner,
    string ItemName, DateTime DocumentDate, decimal Quantity, string UomName, decimal UnitPrice,
    Guid? TaxId = null, decimal DiscountPercentage = 0, decimal DiscountAmount = 0, Guid? WithholdingTaxId = null)
{
    public Guid? DimensionSetId { get; set; }
    public Guid OrderLineId { get; init; }
    public decimal OrderQuantityBase { get; init; }
    public decimal RemainingInvoiceQuantityBase { get; init; }
    public decimal UomConversionFactor { get; init; } = 1m;
}

public sealed record ManualSalesInvoiceLineInput(Guid ItemId, Guid? UomId, string UomName, decimal UomConversionFactor, decimal Quantity, decimal UnitPrice, Guid? DimensionSetId = null);
public sealed record ManualPurchaseInvoiceLineInput(Guid ItemId, Guid? UomId, string UomName, decimal UomConversionFactor, decimal Quantity, decimal UnitCost, Guid? DimensionSetId = null);

public sealed class DestinationDocumentTerms
{
    public Dictionary<Guid, Guid?> LineDimensionSets { get; set; } = new();
    public bool Confirmed { get; set; }
    public Guid? SavedInvoiceId { get; set; }
    public InvoiceRoute Route { get; set; } = new();
    public Guid PartnerId { get; set; }
    public Guid SiteId { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public Guid? TaxId { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public Guid? WithholdingTaxId { get; set; }
    // Purchase-invoice details are entered per destination group.
    public DateTime? InvoiceDate { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public bool InvoiceFullOrderQuantity { get; set; }
}

/// <summary>
/// Owns the document boundaries and source-line links for the purchase and
/// sales workflows.  All conversion methods run in a transaction and validate
/// persisted source rows rather than trusting page-supplied totals.
/// </summary>
public sealed class DocumentWorkflowService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentNumberingService _numbering;
    private readonly GLOperationsService _glOperations;
    private readonly TransactionMappingService _mappings;
    private readonly InventoryService _inventory;

    public DocumentWorkflowService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentNumberingService numbering,
        GLOperationsService glOperations,
        TransactionMappingService mappings,
        InventoryService inventory)
    {
        _dbFactory = dbFactory;
        _numbering = numbering;
        _glOperations = glOperations;
        _mappings = mappings;
        _inventory = inventory;
    }

    public async Task<List<PurchaseRequest>> GetPurchaseRequestsAsync(Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.PurchaseRequests.AsNoTracking()
            .Include(x => x.Vendor).Include(x => x.Warehouse).Include(x => x.Currency)
            .Include(x => x.Lines).ThenInclude(x => x.Item)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.RequestDate).ToListAsync();
    }

    public async Task<List<SalesQuote>> GetSalesQuotesAsync(Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.SalesQuotes.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Warehouse).Include(x => x.Currency)
            .Include(x => x.Lines).ThenInclude(x => x.Item)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Date).ToListAsync();
    }

    public async Task<List<PurchaseInvoice>> GetPurchaseInvoicesAsync(Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Vendor).Include(x => x.Warehouse).Include(x => x.Currency)
            .Include(x => x.Lines).ThenInclude(x => x.Item)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.BillDate).ToListAsync();
    }

    public async Task<string> DeleteNormalSalesInvoiceDraftAsync(Guid companyId, Guid invoiceId, string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var invoice = await db.SalesInvoices
                .FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {invoiceId} AND [CompanyId] = {companyId}")
                .Include(x => x.Lines).FirstOrDefaultAsync();
            if (invoice == null) return "Sales invoice not found.";
            if (invoice.IsDirectInvoice || invoice.Status != SalesInvoiceStatus.Draft || invoice.GLBatchId.HasValue || invoice.InvoiceBatchId.HasValue)
                return "Only an unsubmitted normal invoice draft can be deleted.";
            if (await db.PaymentApplications.AnyAsync(x => x.InvoiceId == invoice.Id)) return "This invoice has a payment application and cannot be deleted.";
            db.SalesInvoices.Remove(invoice);
            db.AuditLogs.Add(new AuditLog { CompanyId = companyId, UserId = userId, Action = "InvoiceDraftDeleted", EntityType = nameof(SalesInvoice), EntityId = invoice.Id, Details = $"Deleted draft {invoice.InvoiceNumber} and released its source quantities.", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (DbUpdateException) { return "The invoice draft could not be deleted because it is in use."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The database is busy. Try deleting the draft again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "The draft is being used by another transaction. Try again."; }
    }

    public async Task<string> DeleteNormalPurchaseInvoiceDraftAsync(Guid companyId, Guid invoiceId, string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var invoice = await db.PurchaseInvoices
                .FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {invoiceId} AND [CompanyId] = {companyId}")
                .Include(x => x.Lines).FirstOrDefaultAsync();
            if (invoice == null) return "Purchase invoice not found.";
            if (invoice.IsDirectBill || invoice.IsPosted || invoice.GLBatchId.HasValue)
                return "Only an unsubmitted normal invoice draft can be deleted.";
            if (await db.VendorPayments.AnyAsync(x => x.PurchaseInvoiceId == invoice.Id)) return "This invoice has a payment and cannot be deleted.";
            db.PurchaseInvoices.Remove(invoice);
            db.AuditLogs.Add(new AuditLog { CompanyId = companyId, UserId = userId, Action = "InvoiceDraftDeleted", EntityType = nameof(PurchaseInvoice), EntityId = invoice.Id, Details = $"Deleted draft {invoice.PurchaseInvoiceNumber} and released its source quantities.", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (DbUpdateException) { return "The invoice draft could not be deleted because it is in use."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The database is busy. Try deleting the draft again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "The draft is being used by another transaction. Try again."; }
    }

    public async Task<List<SalesInvoice>> GetSalesInvoicesAsync(Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        // Lock invoices being finalized before reading their source balances.
        // Two register refreshes must not both add the same invoiced quantity.
        var pendingStatus = (int)SalesInvoiceStatus.InReview;
        var candidates = await db.SalesInvoices
            .FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [CompanyId] = {companyId} AND [Status] = {pendingStatus} AND [GLBatchId] IS NOT NULL")
            .Include(x => x.Lines).ToListAsync();
        var batchIds = candidates.Select(x => x.GLBatchId!.Value).Distinct().ToList();
        var postedIds = await db.GLBatches.Where(x => batchIds.Contains(x.Id) && x.CompanyId == companyId && x.Status == BatchStatus.Posted)
            .Select(x => x.Id).ToListAsync();
        foreach (var invoice in candidates.Where(x => postedIds.Contains(x.GLBatchId!.Value)))
        {
            invoice.Status = SalesInvoiceStatus.Posted;
            var sourceLines = invoice.Lines.Where(x => x.SalesOrderLineId.HasValue).ToList();
            foreach (var source in sourceLines)
            {
                var orderLine = await db.SalesOrderLines.FirstOrDefaultAsync(x => x.Id == source.SalesOrderLineId!.Value);
                if (orderLine != null) orderLine.QtyInvoiced = Math.Min(orderLine.Quantity, orderLine.QtyInvoiced + source.QuantityInvoiced);
            }
            var sourceLineIds = sourceLines.Select(x => x.SalesOrderLineId!.Value).Distinct().ToList();
            var sourceOrderIds = await db.SalesOrderLines
                .Where(x => sourceLineIds.Contains(x.Id))
                .Select(x => x.HeaderId).Distinct().ToListAsync();
            foreach (var sourceOrderId in sourceOrderIds)
            {
                var order = await db.SalesOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == sourceOrderId);
                if (order != null)
                    order.Status = order.Lines.All(x => x.QtyInvoiced >= x.Quantity) ? OrderStatus.Invoiced : OrderStatus.PartiallyInvoiced;
            }
        }
        if (candidates.Any(x => postedIds.Contains(x.GLBatchId!.Value))) await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await db.SalesInvoices.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Warehouse).Include(x => x.Currency)
            .Include(x => x.Lines).ThenInclude(x => x.Item)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Date).ToListAsync();
    }

    public async Task<string> PostSalesInvoiceAsync(Guid invoiceId, string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
var stageError = await StageSalesInvoiceAsync(db, invoiceId, userId);
            if (!string.IsNullOrEmpty(stageError)) return stageError;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        return string.Empty;
        }
        catch (DbUpdateException) { return "The invoice could not be submitted. Its draft and source reservations were retained."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice could not be submitted because the database is busy. Try again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "The invoice could not be submitted because another transaction is using it. Try again."; }
    }

    public async Task<string> StageSalesInvoiceAsync(AppDbContext db, Guid invoiceId, string userId)
    {
        var invoice = await db.SalesInvoices
            .FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {invoiceId}")
            .Include(x => x.Customer)
            .Include(x => x.Lines).ThenInclude(x => x.Item)
            .FirstOrDefaultAsync(x => x.Id == invoiceId);
        if (invoice == null) return "Sales invoice not found.";
        var dimensionError = await DimensionService.ValidateAsync(db, invoice.CompanyId, invoice.Lines.Select(x => x.DimensionSetId), true);
        if (dimensionError.Length > 0) return dimensionError;
        if (invoice.Status == SalesInvoiceStatus.Cancelled) return "Cancelled sales invoices cannot be posted.";
        if (invoice.Status == SalesInvoiceStatus.Posted) return "This sales invoice is already posted.";
        if (!invoice.IsDirectInvoice && invoice.Lines.Any(x => x.ShipmentLineId.HasValue) && invoice.Lines.Any(x => !x.ShipmentLineId.HasValue))
            return "Shipment lines and manual item lines cannot be combined on one invoice. Recreate this draft using one entry method.";
        var routeError = await InvoiceRoutePolicy.ValidateAsync(db, invoice.CompanyId, TransactionTemplateModule.AR,
            new InvoiceRoute { TemplateId = invoice.CustomTransactionTypeId, DebitAccountId = invoice.ReceivablesGlAccountId, CreditAccountId = invoice.DirectIncomeGlAccountId, TaxAccountId = invoice.TaxGLAccountId, DiscountAccountId = invoice.DiscountGlAccountId });
        if (routeError != null) return routeError;
        var currencyError = await DocumentCurrencyPolicy.ValidateAsync(db, invoice.CompanyId, invoice.CurrencyId, invoice.ExchangeRate);
        if (currencyError != null) return currencyError;
        if (invoice.Lines.Count == 0) return "A sales invoice must contain at least one line.";
        if (invoice.Lines.Any(x => x.QuantityInvoiced <= 0 || x.UnitPrice < 0 || string.IsNullOrWhiteSpace(x.Description)))
            return "Every invoice line needs a description, positive quantity and non-negative price.";
        if (!invoice.IsDirectInvoice && invoice.Lines.Any(x => !x.ShipmentLineId.HasValue && (!x.ItemId.HasValue || x.SalesOrderLineId.HasValue)))
            return "A manual invoice line must have an item and cannot claim an order source.";
        if (invoice.GLBatchId.HasValue)
        {
            var existing = await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoice.GLBatchId.Value && x.CompanyId == invoice.CompanyId);
            if (existing?.Status == BatchStatus.Posted) return "This sales invoice is already posted.";
            if (existing != null && !invoice.IsDirectInvoice && invoice.Lines.Any(x => x.ShipmentLineId.HasValue)) return "This invoice already has a GL review batch. Resolve that batch before editing or posting it again.";
        }
        Dictionary<Guid, SalesShipmentLine> shippedLines = new();
        if (!invoice.IsDirectInvoice)
        {
            var sourceLines = invoice.Lines.Where(x => x.ShipmentLineId.HasValue).ToList();
            var lineIds = sourceLines.Select(x => x.ShipmentLineId!.Value).ToList();
            if (lineIds.Distinct().Count() != lineIds.Count) return "A shipment line cannot appear twice on one invoice.";
            if (await db.SalesInvoiceLines.AnyAsync(x => x.SalesInvoiceId != invoice.Id && x.ShipmentLineId.HasValue && lineIds.Contains(x.ShipmentLineId.Value)))
                return "A source shipment line has already been invoiced.";
            shippedLines = await db.SalesShipmentLines.Where(x => lineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            if (shippedLines.Count != lineIds.Count || sourceLines.Any(x => x.QuantityInvoiced < shippedLines[x.ShipmentLineId!.Value].QtyShipped))
                return "Invoice quantities must cover the selected shipment lines.";
            if (shippedLines.Values.Any(x => x.SalesInvoiceLineId.HasValue || !x.SalesOrderLineId.HasValue))
                return "Invoice-sourced shipments cannot be invoiced again.";
            if (sourceLines.Any(x => x.SalesOrderLineId != shippedLines[x.ShipmentLineId!.Value].SalesOrderLineId
                || x.ItemId != shippedLines[x.ShipmentLineId!.Value].ItemId
                || x.UomId != shippedLines[x.ShipmentLineId!.Value].UomId
                || x.UomConversionFactor != shippedLines[x.ShipmentLineId!.Value].UomConversionFactor))
                return "Every invoice item, order line and unit must match its selected shipment line.";
            var shipmentIds = shippedLines.Values.Select(x => x.ShipmentId).Distinct().ToList();
            var shipments = await db.SalesShipments.Where(x => shipmentIds.Contains(x.Id) && x.CompanyId == invoice.CompanyId).ToListAsync();
            if (shipments.Count != shipmentIds.Count || shipments.Any(x => x.Status != ShipmentStatus.Shipped || !x.ShipmentBatchId.HasValue))
                return "Only completed shipments with a GL batch can be invoiced.";
            var shipmentBatchIds = shipments.Select(x => x.ShipmentBatchId!.Value).Distinct().ToList();
            if (await db.GLBatches.CountAsync(x => shipmentBatchIds.Contains(x.Id) && x.CompanyId == invoice.CompanyId && x.Status == BatchStatus.Posted) != shipmentBatchIds.Count)
                return "Every source shipment must be posted to the General Ledger first.";
            var orderLineIds = sourceLines.Where(x => x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).Distinct().ToList();
            var orderQuantities = await db.SalesOrderLines.Where(x => orderLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Quantity);
            var priorQuantities = await db.SalesInvoiceLines.Where(x => x.SalesInvoiceId != invoice.Id && x.SalesOrderLineId.HasValue && orderLineIds.Contains(x.SalesOrderLineId.Value))
                .GroupBy(x => x.SalesOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityInvoiced) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
            foreach (var group in sourceLines.GroupBy(x => x.SalesOrderLineId!.Value))
            {
                if (!orderQuantities.TryGetValue(group.Key, out var ordered)) return "A source sales order line no longer exists.";
                var total = priorQuantities.GetValueOrDefault(group.Key) + group.Sum(x => x.QuantityInvoiced);
                var hasExtra = group.Any(x => x.QuantityInvoiced > shippedLines[x.ShipmentLineId!.Value].QtyShipped);
                if (total > ordered || (hasExtra && total != ordered))
                    return "Invoice either the shipped quantity or the entire remaining order quantity for each source line.";
            }
        }
        if (invoice.Customer == null || invoice.Customer.CompanyId != invoice.CompanyId) return "Customer master data is missing or belongs to another company.";
        if (!invoice.IsDirectInvoice)
        {
            var sourceOrderLineIds = invoice.Lines.Where(x => x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).Distinct().ToList();
            var sourceOrders = await db.SalesOrderLines.Where(x => sourceOrderLineIds.Contains(x.Id))
                .Join(db.SalesOrders, line => line.HeaderId, order => order.Id, (line, order) => order).ToListAsync();
            if (sourceOrders.Count != sourceOrderLineIds.Count || sourceOrders.Any(x => x.CompanyId != invoice.CompanyId || x.CustomerId != invoice.CustomerId
                || x.CurrencyId != invoice.CurrencyId))
                return "Invoice customer and currency must match every source order line.";
            if (invoice.Lines.Any(x => x.Item == null || x.Item.CompanyId != invoice.CompanyId))
                return "Every invoice item must belong to this company.";
        }
        var invoiceDate = invoice.Date.ToDateTime(TimeOnly.MinValue);
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(x => x.CompanyId == invoice.CompanyId && x.StartDate <= DateOnly.FromDateTime(invoiceDate) && x.EndDate >= DateOnly.FromDateTime(invoiceDate));
        if (period == null || period.IsClosed) return "The sales invoice date is outside an open accounting period.";

        var rate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1m;
        var grossForeign = invoice.Lines.Sum(x => x.Amount);
        var discountForeign = invoice.DiscountPercentage > 0
            ? grossForeign * invoice.DiscountPercentage / 100m
            : Math.Min(invoice.DiscountAmount, grossForeign);
        var discountBase = Math.Round(discountForeign * rate, 2, MidpointRounding.AwayFromZero);
        decimal revenueBase = 0;
        var glLines = new List<GLJournalLine>();
        var customMapping = invoice.CustomTransactionTypeId.HasValue
            ? await db.TransactionGlMappings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == invoice.CompanyId
                && x.CustomTransactionTypeId == invoice.CustomTransactionTypeId.Value)
            : null;
        foreach (var line in invoice.Lines)
        {
            var item = line.Item ?? await db.Items.Include(x => x.UomConversions).FirstOrDefaultAsync(x => x.Id == line.ItemId);
            if (!invoice.IsDirectInvoice && item == null) return "An invoiced item no longer exists.";
            var revenueAccount = invoice.DirectIncomeGlAccountId.GetValueOrDefault();
            if (revenueAccount == Guid.Empty) revenueAccount = line.RevenueGlAccountId.GetValueOrDefault();
            if (revenueAccount == Guid.Empty && customMapping?.OverrideCreditGlAccountId is Guid customRevenue) revenueAccount = customRevenue;
            if (revenueAccount == Guid.Empty && item != null) revenueAccount = item.SalesIncomeAccountId;
            if (!invoice.DirectIncomeGlAccountId.HasValue && !invoice.CustomTransactionTypeId.HasValue) revenueAccount = await _mappings.GetMappedAccountAsync(invoice.CompanyId,
                invoice.IsDirectInvoice ? SystemTransactionType.DirectSalesInvoice : SystemTransactionType.SalesInvoice,
                false, revenueAccount);
            if (revenueAccount == Guid.Empty) return item != null
                ? $"Item '{item.Name}' has no sales income account."
                : "A revenue account is required for every direct invoice line.";
            var lineBase = Math.Round(line.Amount * rate, 2, MidpointRounding.AwayFromZero);
            revenueBase += lineBase;
            glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = revenueAccount, Debit = 0, Credit = lineBase,
                Reference = $"Sales {invoice.InvoiceNumber}: {line.Description ?? item?.Name ?? "Direct line"}" });
        }
        if (discountBase > 0)
        {
            var discountAccount = invoice.DiscountGlAccountId.GetValueOrDefault();
            if (discountAccount == Guid.Empty) discountAccount = await _mappings.GetMappedAccountAsync(invoice.CompanyId, SystemTransactionType.DiscountAllowed, true, discountAccount);
            if (discountAccount == Guid.Empty) return "A discount is present but no Discount Allowed account is configured.";
            glLines.Add(new GLJournalLine { SegCoaId = discountAccount, Debit = discountBase, Credit = 0, Reference = $"Discount {invoice.InvoiceNumber}" });
        }

        decimal taxBaseTotal = 0;
        decimal taxForeignTotal = 0;
        foreach (var taxGroup in invoice.Lines.GroupBy(x => x.TaxId ?? invoice.TaxId))
        {
            if (!taxGroup.Key.HasValue) continue;
            var tax = await db.Taxes.FirstOrDefaultAsync(x => x.Id == taxGroup.Key.Value && x.CompanyId == invoice.CompanyId);
            if (tax == null) return "A tax selected on the invoice is no longer available.";
            var taxableForeign = taxGroup.Sum(x => x.Amount);
            var proportionalDiscount = grossForeign > 0 ? discountForeign * taxableForeign / grossForeign : 0m;
            var taxBase = Math.Round((taxableForeign - proportionalDiscount) * rate * tax.Per / 100m, 2, MidpointRounding.AwayFromZero);
            if (taxBase <= 0) continue;
            var lineTaxAccountId = taxGroup.Select(x => x.TaxGLAccountId).FirstOrDefault(x => x.HasValue);
            var taxAccount = invoice.TaxGLAccountId ?? lineTaxAccountId ?? tax.GLAccountId.GetValueOrDefault();
            if (taxAccount == Guid.Empty) return $"Tax '{tax.TaxName}' has no GL account.";
            var taxLegs = new List<GLJournalLine> { new() { SegCoaId = taxAccount, Credit = taxBase, Reference = $"{tax.TaxCode} {invoice.InvoiceNumber}" } };
            DimensionService.AllocateSummaryLines(taxLegs, taxGroup.Select(x => (x.DimensionSetId, x.Amount)));
            glLines.AddRange(taxLegs);
            taxBaseTotal += taxBase;
            taxForeignTotal += taxableForeign - proportionalDiscount > 0
                ? (taxableForeign - proportionalDiscount) * tax.Per / 100m
                : 0m;
        }
        var arAccount = invoice.ReceivablesGlAccountId.GetValueOrDefault();
        if (arAccount == Guid.Empty) arAccount = customMapping?.OverrideDebitGlAccountId ?? Guid.Empty;
        if (arAccount == Guid.Empty) arAccount = invoice.Customer.ReceivablesAccountId.GetValueOrDefault();
        if (!invoice.ReceivablesGlAccountId.HasValue && !invoice.CustomTransactionTypeId.HasValue) arAccount = await _mappings.GetMappedAccountAsync(invoice.CompanyId,
            invoice.IsDirectInvoice ? SystemTransactionType.DirectSalesInvoice : SystemTransactionType.SalesInvoice,
            true, arAccount);
        if (arAccount == Guid.Empty) return "Customer accounts receivable account is not configured.";
        glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = revenueBase - discountBase + taxBaseTotal, Credit = 0, Reference = $"AR {invoice.InvoiceNumber}" });

        DimensionService.AllocateSummaryLines(glLines, invoice.Lines.Select(x => (x.DimensionSetId, x.Amount)));
        var (error, batchId) = invoice.GLBatchId.HasValue
            ? await _glOperations.RestageSubledgerBatchAsync(
                db, invoice.CompanyId, invoice.GLBatchId.Value, invoice.Date, $"Sales Invoice {invoice.InvoiceNumber} - {invoice.Customer.Name}",
                invoice.InvoiceNumber, glLines, userId, invoice.ProjectId)
            : await _glOperations.StageNewSubledgerBatchAsync(
                db, invoice.CompanyId, invoice.Date, invoice.InvoiceNumber, $"Sales Invoice {invoice.InvoiceNumber} - {invoice.Customer.Name}",
                invoice.InvoiceNumber, glLines, userId, inheritedProjectId: invoice.ProjectId);
        if (!string.IsNullOrWhiteSpace(error)) return $"GL staging error: {error}";
        if (!batchId.HasValue) return "GL staging did not create a review batch.";
        invoice.GLBatchId = batchId;
        invoice.InvoiceBatchId = batchId;
        invoice.Status = SalesInvoiceStatus.InReview;
        invoice.TotalAmountForeign = grossForeign - discountForeign + taxForeignTotal;
        invoice.TotalAmount = Math.Round((revenueBase - discountBase + taxBaseTotal), 2, MidpointRounding.AwayFromZero);
        invoice.PostedDate = null;

        return string.Empty;
    }

    public async Task<List<WorkflowLineOption>> GetReceiptLinesReadyToInvoiceAsync(Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await (from line in db.GoodsReceiptLines.AsNoTracking()
                      join receipt in db.GoodsReceipts.AsNoTracking() on line.GoodsReceiptId equals receipt.Id
                      join poLine in db.PurchaseOrderLines.AsNoTracking() on line.PurchaseOrderLineId equals poLine.Id
                      join po in db.PurchaseOrders.AsNoTracking() on poLine.PurchaseOrderId equals po.Id
                      join vendor in db.Vendors.AsNoTracking() on po.VendorId equals vendor.Id
                      join item in db.Items.Include(x => x.UomConversions).AsNoTracking() on poLine.ItemId equals item.Id
                      where receipt.CompanyId == companyId && receipt.GLBatchId.HasValue
                          && db.GLBatches.Any(batch => batch.Id == receipt.GLBatchId.Value && batch.CompanyId == companyId && batch.Status == BatchStatus.Posted)
                          && line.QuantityReceived > 0
                          && !line.PurchaseInvoiceLineId.HasValue
                          && !db.PurchaseInvoiceLines.Any(invoiceLine => invoiceLine.GoodsReceiptLineId == line.Id)
                      orderby receipt.DateReceived descending
                      select new WorkflowLineOption(line.Id, receipt.Id, po.VendorId, receipt.WarehouseId, po.CurrencyId, po.ExchangeRate, receipt.GrnNumber, vendor.Name, item.Name,
                          receipt.DateReceived, UomConversion.FromBase(line.QuantityReceived, line.UomConversionFactor), line.UomName, UomConversion.PriceFromBase(poLine.UnitCost, line.UomConversionFactor),
                          po.TaxId, po.DiscountPercentage, po.DiscountAmount, po.WithholdingTaxId) { DimensionSetId = line.DimensionSetId, OrderLineId = poLine.Id, OrderQuantityBase = poLine.QuantityOrdered, UomConversionFactor = line.UomConversionFactor })
            .ToListAsync();
        var ids = rows.Select(x => x.OrderLineId).Distinct().ToList();
        var reserved = await db.PurchaseInvoiceLines.AsNoTracking()
            .Where(x => x.PurchaseOrderLineId.HasValue && ids.Contains(x.PurchaseOrderLineId.Value))
            .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityBilled) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty);
        return rows.Select(x => x with { RemainingInvoiceQuantityBase = Math.Max(0, x.OrderQuantityBase - reserved.GetValueOrDefault(x.OrderLineId)) })
            .Where(x => UomConversion.ToBase(x.Quantity, x.UomConversionFactor) <= x.RemainingInvoiceQuantityBase && x.RemainingInvoiceQuantityBase > 0).ToList();
    }

    public async Task<List<WorkflowLineOption>> GetShipmentLinesReadyToInvoiceAsync(Guid companyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await (from line in db.SalesShipmentLines.AsNoTracking()
                      join shipment in db.SalesShipments.AsNoTracking() on line.ShipmentId equals shipment.Id
                      join orderLine in db.SalesOrderLines.AsNoTracking() on line.SalesOrderLineId equals orderLine.Id
                      join order in db.SalesOrders.AsNoTracking() on orderLine.HeaderId equals order.Id
                      join item in db.Items.Include(x => x.UomConversions).AsNoTracking() on line.ItemId equals item.Id
                      where shipment.CompanyId == companyId && shipment.Status == ShipmentStatus.Shipped
                          && shipment.ShipmentBatchId.HasValue
                          && db.GLBatches.Any(batch => batch.Id == shipment.ShipmentBatchId.Value && batch.CompanyId == companyId && batch.Status == BatchStatus.Posted)
                          && line.QtyShipped > 0
                          && !line.SalesInvoiceLineId.HasValue
                          && !db.SalesInvoiceLines.Any(invoiceLine => invoiceLine.ShipmentLineId == line.Id)
                      orderby shipment.ShippedDate descending
                      select new WorkflowLineOption(line.Id, shipment.Id, order.CustomerId, shipment.WarehouseId, order.CurrencyId, order.ExchangeRate, shipment.ShipmentNumber, order.Customer.Name, item.Name,
                          shipment.ShippedDate ?? shipment.CreatedDate, UomConversion.FromBase(line.QtyShipped, line.UomConversionFactor), line.UomName, UomConversion.PriceFromBase(orderLine.UnitPrice, line.UomConversionFactor),
                          order.TaxId, order.DiscountPercentage, order.DiscountAmount, order.WithholdingTaxId) { DimensionSetId = line.DimensionSetId, OrderLineId = orderLine.Id, OrderQuantityBase = orderLine.Quantity, UomConversionFactor = line.UomConversionFactor })
            .ToListAsync();
        var ids = rows.Select(x => x.OrderLineId).Distinct().ToList();
        var reserved = await db.SalesInvoiceLines.AsNoTracking()
            .Where(x => x.SalesOrderLineId.HasValue && ids.Contains(x.SalesOrderLineId.Value))
            .GroupBy(x => x.SalesOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityInvoiced) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty);
        return rows.Select(x => x with { RemainingInvoiceQuantityBase = Math.Max(0, x.OrderQuantityBase - reserved.GetValueOrDefault(x.OrderLineId)) })
            .Where(x => UomConversion.ToBase(x.Quantity, x.UomConversionFactor) <= x.RemainingInvoiceQuantityBase && x.RemainingInvoiceQuantityBase > 0).ToList();
    }

    public async Task<string> SavePurchaseRequestAsync(PurchaseRequest request)
    {
        if (request.CompanyId == Guid.Empty) return "Company is required.";
        if (request.VendorId == Guid.Empty) return "Supplier is required.";
        if (request.CurrencyId == Guid.Empty) return "Currency is required.";
        if (request.RequestDate == default) return "Request date is required.";
        if (request.Lines.Count == 0) return "Add at least one request line.";
        if (request.Lines.Any(x => x.ItemId == Guid.Empty || x.QuantityRequested <= 0 || x.UnitCost < 0))
            return "Each request line needs an item, a quantity greater than zero, and a non-negative unit cost.";
        if (request.ExchangeRate <= 0 || request.DiscountPercentage < 0 || request.DiscountPercentage > 100 || request.DiscountAmount < 0 || (request.DiscountPercentage > 0 && request.DiscountAmount > 0)) return "Invalid exchange rate or discount terms.";

        await using var db = await _dbFactory.CreateDbContextAsync();
        var dimensionError = await DimensionService.ValidateAsync(db, request.CompanyId, request.Lines.Select(x => x.DimensionSetId), request.Status != PurchaseRequestStatus.Draft);
        if (dimensionError.Length > 0) return dimensionError;
        if (request.ProjectId.HasValue && !await db.Projects.AnyAsync(x => x.Id == request.ProjectId && x.CompanyId == request.CompanyId)) return "The selected project is unavailable for this company.";
        var requestCurrency = await db.PurchaseRequests.Where(x => x.Id == request.Id && x.CompanyId == request.CompanyId && x.VendorId == request.VendorId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync()
            ?? await db.Vendors.Where(x => x.Id == request.VendorId && x.CompanyId == request.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
        var requestCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(db, request.CompanyId, request.CurrencyId, request.ExchangeRate, requestCurrency);
        if (requestCurrencyError != null) return requestCurrencyError;
        if (request.TaxId.HasValue)
        {
            var tax = await db.Taxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.TaxId && x.CompanyId == request.CompanyId);
            if (tax == null) return "The selected tax is unavailable for this company.";
            request.TaxGlAccountId = tax.GLAccountId;
        }
        else request.TaxGlAccountId = null;
        if (request.WithholdingTaxId.HasValue)
        {
            var withholding = await db.WithholdingTaxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.WithholdingTaxId && x.CompanyId == request.CompanyId);
            if (withholding == null) return "The selected withholding tax is unavailable for this company.";
            if (withholding.WithholdingGlAccountId == Guid.Empty || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == withholding.WithholdingGlAccountId && x.CompanyId == request.CompanyId && x.IsActive))
                return "The selected withholding tax needs an active purchase GL account.";
            request.WithholdingTaxName = withholding.Name;
            request.WithholdingPercentage = withholding.PercentageValue;
            request.WithholdingGlAccountId = withholding.WithholdingGlAccountId;
        }
        else
        {
            request.WithholdingTaxName = null;
            request.WithholdingPercentage = 0;
            request.WithholdingGlAccountId = null;
        }
        if (!await db.Vendors.AnyAsync(x => x.Id == request.VendorId && x.CompanyId == request.CompanyId)) return "The selected supplier is not in this company.";
        if (request.WarehouseId.HasValue && !await db.Warehouses.AnyAsync(x => x.Id == request.WarehouseId && x.CompanyId == request.CompanyId
            && (x.Type == WarehouseType.Both || x.Type == WarehouseType.Purchase))) return "The selected site is not available for purchasing in this company.";
        if (!await db.Currencies.AnyAsync(x => x.Id == request.CurrencyId && x.CompanyId == request.CompanyId)) return "The selected currency is not in this company.";
        var itemIds = request.Lines.Select(x => x.ItemId).Distinct().ToList();
        if (await db.Items.CountAsync(x => itemIds.Contains(x.Id) && x.CompanyId == request.CompanyId) != itemIds.Count)
            return "One or more request items do not belong to this company.";
        var uomError = await ValidateSourceUomsAsync(db, request.CompanyId, request.Lines.Select(x => (x.ItemId, x.UomId, x.UomConversionFactor)).ToList());
        if (uomError != null) return uomError;

        if (request.Id == Guid.Empty) request.Id = Guid.NewGuid();
        var existing = await db.PurchaseRequests.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.CompanyId == request.CompanyId);
        var existingRequestLineIds = existing?.Lines.Select(x => x.Id).ToList() ?? new List<Guid>();
        if (existing != null && (existing.Status is not (PurchaseRequestStatus.Draft or PurchaseRequestStatus.Open)
            || await db.PurchaseOrderLines.AnyAsync(x => x.PurchaseRequestLineId.HasValue && existingRequestLineIds.Contains(x.PurchaseRequestLineId.Value))))
            return "A converted purchase request cannot be edited.";
        if (string.IsNullOrWhiteSpace(request.RequestNumber))
            request.RequestNumber = await _numbering.GenerateAndReserveNextNumberAsync(request.CompanyId, SequenceTargetType.AP_Request, request.RequestDate);

        if (existing == null)
        {
            request.Status = request.Status == PurchaseRequestStatus.Draft ? PurchaseRequestStatus.Draft : PurchaseRequestStatus.Open;
            request.Lines.ForEach(x => { x.Id = Guid.NewGuid(); x.PurchaseRequestId = request.Id; });
            db.PurchaseRequests.Add(request);
        }
        else
        {
            existing.VendorId = request.VendorId;
            existing.WarehouseId = request.WarehouseId;
            existing.CurrencyId = request.CurrencyId;
            existing.RequestDate = request.RequestDate;
            existing.Status = request.Status == PurchaseRequestStatus.Draft ? PurchaseRequestStatus.Draft : PurchaseRequestStatus.Open;
            existing.ProjectId = request.ProjectId;
            existing.ExchangeRate = request.ExchangeRate;
            existing.TaxId = request.TaxId;
            existing.TaxGlAccountId = request.TaxGlAccountId;
            existing.DiscountPercentage = request.DiscountPercentage;
            existing.DiscountAmount = request.DiscountAmount;
            existing.DiscountGlAccountId = request.DiscountGlAccountId;
            existing.WithholdingTaxId = request.WithholdingTaxId;
            existing.WithholdingTaxName = request.WithholdingTaxName;
            existing.WithholdingPercentage = request.WithholdingPercentage;
            existing.WithholdingGlAccountId = request.WithholdingGlAccountId;
            db.PurchaseRequestLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            foreach (var source in request.Lines)
            {
                source.Id = Guid.NewGuid();
                source.PurchaseRequestId = existing.Id;
                // Insert scalar copies so a detached editor graph is never reattached.
                var line = new PurchaseRequestLine();
                db.Entry(line).CurrentValues.SetValues(source);
                db.PurchaseRequestLines.Add(line);
            }
        }
        await db.SaveChangesAsync();
        return string.Empty;
    }

    public async Task<string> SaveSalesQuoteAsync(SalesQuote quote)
    {
        if (quote.CompanyId == Guid.Empty) return "Company is required.";
        if (quote.CustomerId == Guid.Empty) return "Customer is required.";
        if (quote.CurrencyId == Guid.Empty) return "Currency is required.";
        if (quote.Date == default) return "Quote date is required.";
        if (quote.Lines.Count == 0) return "Add at least one quote line.";
        if (quote.Lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || (!x.ItemId.HasValue && string.IsNullOrWhiteSpace(x.Description))))
            return "Each quote line needs a positive quantity and either an item or a description.";
        if (quote.ExchangeRate <= 0 || quote.DiscountPercentage < 0 || quote.DiscountPercentage > 100 || quote.DiscountAmount < 0 || (quote.DiscountPercentage > 0 && quote.DiscountAmount > 0)) return "Invalid exchange rate or discount terms.";
        if (quote.ValidUntil.HasValue && quote.ValidUntil < quote.Date) return "The quote expiry date cannot precede its date.";

        await using var db = await _dbFactory.CreateDbContextAsync();
        var dimensionError = await DimensionService.ValidateAsync(db, quote.CompanyId, quote.Lines.Select(x => x.DimensionSetId), quote.Status != SalesQuoteStatus.Draft);
        if (dimensionError.Length > 0) return dimensionError;
        if (quote.ProjectId.HasValue && !await db.Projects.AnyAsync(x => x.Id == quote.ProjectId && x.CompanyId == quote.CompanyId)) return "The selected project is unavailable for this company.";
        var quoteCurrency = await db.SalesQuotes.Where(x => x.Id == quote.Id && x.CompanyId == quote.CompanyId && x.CustomerId == quote.CustomerId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync()
            ?? await db.Customers.Where(x => x.Id == quote.CustomerId && x.CompanyId == quote.CompanyId).Select(x => (Guid?)x.CurrencyId).FirstOrDefaultAsync();
        var quoteCurrencyError = await DocumentCurrencyPolicy.ValidateAsync(db, quote.CompanyId, quote.CurrencyId, quote.ExchangeRate, quoteCurrency);
        if (quoteCurrencyError != null) return quoteCurrencyError;
        if (quote.TaxId.HasValue)
        {
            var tax = await db.Taxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == quote.TaxId && x.CompanyId == quote.CompanyId);
            if (tax == null) return "The selected tax is unavailable for this company.";
            quote.TaxGlAccountId = tax.GLAccountId;
        }
        else quote.TaxGlAccountId = null;
        if (quote.WithholdingTaxId.HasValue)
        {
            var withholding = await db.WithholdingTaxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == quote.WithholdingTaxId && x.CompanyId == quote.CompanyId);
            if (withholding == null) return "The selected withholding tax is unavailable for this company.";
            if (!withholding.SalesGlAccountId.HasValue || withholding.SalesGlAccountId == Guid.Empty || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == withholding.SalesGlAccountId && x.CompanyId == quote.CompanyId && x.IsActive))
                return "The selected withholding tax needs an active sales receivable account.";
            quote.WithholdingTaxName = withholding.Name;
            quote.WithholdingPercentage = withholding.PercentageValue;
            quote.WithholdingSalesGlAccountId = withholding.SalesGlAccountId;
        }
        else
        {
            quote.WithholdingTaxName = null;
            quote.WithholdingPercentage = 0;
            quote.WithholdingSalesGlAccountId = null;
        }
        if (!await db.Customers.AnyAsync(x => x.Id == quote.CustomerId && x.CompanyId == quote.CompanyId)) return "The selected customer is not in this company.";
        if (quote.WarehouseId.HasValue && !await db.Warehouses.AnyAsync(x => x.Id == quote.WarehouseId && x.CompanyId == quote.CompanyId
            && (x.Type == WarehouseType.Both || x.Type == WarehouseType.Sales))) return "The selected site is not available for sales in this company.";
        if (!await db.Currencies.AnyAsync(x => x.Id == quote.CurrencyId && x.CompanyId == quote.CompanyId)) return "The selected currency is not in this company.";
        var itemIds = quote.Lines.Where(x => x.ItemId.HasValue).Select(x => x.ItemId!.Value).Distinct().ToList();
        if (await db.Items.CountAsync(x => itemIds.Contains(x.Id) && x.CompanyId == quote.CompanyId) != itemIds.Count)
            return "One or more quote items do not belong to this company.";
        var uomError = await ValidateSourceUomsAsync(db, quote.CompanyId, quote.Lines.Where(x => x.ItemId.HasValue).Select(x => (x.ItemId!.Value, x.UomId, x.UomConversionFactor)).ToList());
        if (uomError != null) return uomError;

        if (quote.Id == Guid.Empty) quote.Id = Guid.NewGuid();
        var existing = await db.SalesQuotes.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == quote.Id && x.CompanyId == quote.CompanyId);
        var existingQuoteLineIds = existing?.Lines.Select(x => x.Id).ToList() ?? new List<Guid>();
        if (existing != null && (existing.Status is not (SalesQuoteStatus.Draft or SalesQuoteStatus.Open)
            || await db.SalesOrderLines.AnyAsync(x => x.SalesQuoteLineId.HasValue && existingQuoteLineIds.Contains(x.SalesQuoteLineId.Value))))
            return "A converted sales quote cannot be edited.";
        if (string.IsNullOrWhiteSpace(quote.QuoteNumber))
            quote.QuoteNumber = await _numbering.GenerateAndReserveNextNumberAsync(quote.CompanyId, SequenceTargetType.AR_Quote, quote.Date.ToDateTime(TimeOnly.MinValue));

        if (existing == null)
        {
            quote.Status = quote.Status == SalesQuoteStatus.Draft ? SalesQuoteStatus.Draft : SalesQuoteStatus.Open;
            quote.Lines.ForEach(x => { x.Id = Guid.NewGuid(); x.SalesQuoteId = quote.Id; });
            db.SalesQuotes.Add(quote);
        }
        else
        {
            existing.CustomerId = quote.CustomerId;
            existing.WarehouseId = quote.WarehouseId;
            existing.CurrencyId = quote.CurrencyId;
            existing.Date = quote.Date;
            existing.ValidUntil = quote.ValidUntil;
            existing.Status = quote.Status == SalesQuoteStatus.Draft ? SalesQuoteStatus.Draft : SalesQuoteStatus.Open;
            existing.ProjectId = quote.ProjectId;
            existing.ExchangeRate = quote.ExchangeRate;
            existing.TaxId = quote.TaxId;
            existing.TaxGlAccountId = quote.TaxGlAccountId;
            existing.DiscountPercentage = quote.DiscountPercentage;
            existing.DiscountAmount = quote.DiscountAmount;
            existing.DiscountGlAccountId = quote.DiscountGlAccountId;
            existing.WithholdingTaxId = quote.WithholdingTaxId;
            existing.WithholdingTaxName = quote.WithholdingTaxName;
            existing.WithholdingPercentage = quote.WithholdingPercentage;
            existing.WithholdingSalesGlAccountId = quote.WithholdingSalesGlAccountId;
            db.SalesQuoteLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            foreach (var source in quote.Lines)
            {
                source.Id = Guid.NewGuid();
                source.SalesQuoteId = existing.Id;
                // Assigned GUIDs must be explicitly added, rather than inferred as updates.
                var line = new SalesQuoteLine();
                db.Entry(line).CurrentValues.SetValues(source);
                db.SalesQuoteLines.Add(line);
            }
        }
        await db.SaveChangesAsync();
        return string.Empty;
    }

    /// <summary>Converts selected whole request lines, grouping only by supplier, site and currency.</summary>
    public async Task<string> ConvertPurchaseRequestLinesAsync(Guid companyId, IReadOnlyCollection<Guid> selectedLineIds, IReadOnlyCollection<DestinationDocumentTerms>? destinationTerms = null)
    {
        var validation = ValidateSelection(selectedLineIds);
        if (validation != null) return validation;
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var lines = await db.PurchaseRequestLines.Include(x => x.PurchaseRequest)
            .Where(x => selectedLineIds.Contains(x.Id) && x.PurchaseRequest!.CompanyId == companyId).ToListAsync();
        if (lines.Count != selectedLineIds.Count) return "A selected request line was not found in this company.";
        var dimensionError = await DimensionService.ValidateAsync(db, companyId, lines.Select(x => x.DimensionSetId), true);
        if (dimensionError.Length > 0) return dimensionError;
        if (lines.Any(x => x.ItemId == Guid.Empty || x.QuantityRequested <= 0 || x.UnitCost < 0)) return "Selected request lines need valid items, positive quantities and non-negative costs.";
        if (lines.Any(x => x.PurchaseRequest == null || x.PurchaseRequest.Status is PurchaseRequestStatus.Cancelled or PurchaseRequestStatus.Converted))
            return "Cancelled or fully converted requests cannot be converted again.";
        var alreadyOrderedRequestLineIds = await db.PurchaseOrderLines
            .Where(x => x.PurchaseRequestLineId.HasValue && selectedLineIds.Contains(x.PurchaseRequestLineId.Value))
            .Select(x => x.PurchaseRequestLineId!.Value).ToListAsync();
        if (alreadyOrderedRequestLineIds.Count > 0) return "One or more selected request lines already belong to a purchase order.";
        if (lines.Any(x => !x.PurchaseRequest!.WarehouseId.HasValue || x.PurchaseRequest.CurrencyId == Guid.Empty))
            return "Every request must have a site and currency before it can be converted.";

        foreach (var group in lines.GroupBy(x => new
        {
            x.PurchaseRequest!.CompanyId,
            x.PurchaseRequest.VendorId,
            WarehouseId = x.PurchaseRequest.WarehouseId!.Value,
            x.PurchaseRequest.CurrencyId
        }))
        {
            var requests = group.Select(x => x.PurchaseRequest!).DistinctBy(x => x.Id).ToList();
            if (destinationTerms == null && !HasCompatiblePurchaseTerms(requests)) return "Selected requests share supplier, site and currency but have different tax, discount or withholding settings. Convert compatible lines together.";
            var first = requests[0];
            var (termsError, selectedTerms, selectedTax, selectedWithholding) = await ResolveTermsAsync(db, companyId, destinationTerms, group.Key.VendorId, group.Key.WarehouseId, group.Key.CurrencyId);
            if (termsError != null) return termsError;
            if (selectedWithholding != null && (selectedWithholding.WithholdingGlAccountId == Guid.Empty || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == selectedWithholding.WithholdingGlAccountId && x.CompanyId == companyId && x.IsActive)))
                return "The selected withholding tax needs an active purchase GL account.";
            var number = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AP_PurchaseOrder);
            var order = new PurchaseOrder
            {
                Id = Guid.NewGuid(), CompanyId = companyId, VendorId = group.Key.VendorId,
                WarehouseId = group.Key.WarehouseId, CurrencyId = group.Key.CurrencyId,
                OrderNumber = number, OrderDate = DateTime.Now, Status = PurchaseOrderStatus.Open,
                ProjectId = first.ProjectId, ExchangeRate = first.ExchangeRate,
                TaxId = first.TaxId, TaxGLAccountId = first.TaxGlAccountId,
                DiscountPercentage = first.DiscountPercentage, DiscountAmount = first.DiscountAmount,
                DiscountGlAccountId = first.DiscountGlAccountId,
                WithholdingTaxId = first.WithholdingTaxId, WithholdingTaxName = first.WithholdingTaxName,
                WithholdingPercentage = first.WithholdingPercentage, WithholdingAmountForeign = first.WithholdingAmountForeign,
                WithholdingAmount = first.WithholdingAmount, WithholdingGlAccountId = first.WithholdingGlAccountId
            };
            if (selectedTerms != null)
            {
                order.ExchangeRate = selectedTerms.ExchangeRate;
                order.TaxId = selectedTax?.Id;
                order.TaxGLAccountId = selectedTax?.GLAccountId;
                order.DiscountPercentage = selectedTerms.DiscountPercentage;
                order.DiscountAmount = selectedTerms.DiscountAmount;
                order.WithholdingTaxId = selectedWithholding?.Id;
                order.WithholdingTaxName = selectedWithholding?.Name;
                order.WithholdingPercentage = selectedWithholding?.PercentageValue ?? 0;
                order.WithholdingGlAccountId = selectedWithholding?.WithholdingGlAccountId;
                order.WithholdingAmountForeign = 0;
                order.WithholdingAmount = 0;
            }
            foreach (var source in group)
            {
                order.Lines.Add(new PurchaseOrderLine
                { DimensionSetId = source.DimensionSetId,
                    Id = Guid.NewGuid(), PurchaseOrderId = order.Id, PurchaseRequestLineId = source.Id,
                    ItemId = source.ItemId, QuantityOrdered = source.QuantityRequested, UnitCost = source.UnitCost,
                    UomId = source.UomId, UomName = source.UomName, UomConversionFactor = source.UomConversionFactor
                });
            }
            db.PurchaseOrders.Add(order);
            foreach (var request in requests)
            {
                var requestLines = await db.PurchaseRequestLines.Where(x => x.PurchaseRequestId == request.Id).ToListAsync();
                var selectedRequestIds = group.Where(x => x.PurchaseRequestId == request.Id).Select(x => x.Id).ToHashSet();
                var mappedIds = await db.PurchaseOrderLines.Where(x => x.PurchaseRequestLineId.HasValue)
                    .Select(x => x.PurchaseRequestLineId!.Value).ToListAsync();
                bool fullyConverted = requestLines.All(x => mappedIds.Contains(x.Id) || selectedRequestIds.Contains(x.Id));
                request.Status = fullyConverted ? PurchaseRequestStatus.Converted : PurchaseRequestStatus.PartiallyConverted;
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return string.Empty;
    }

    /// <summary>Converts selected whole quote lines, grouping only by customer, site and currency.</summary>
    public async Task<string> ConvertSalesQuoteLinesAsync(Guid companyId, IReadOnlyCollection<Guid> selectedLineIds, IReadOnlyCollection<DestinationDocumentTerms>? destinationTerms = null)
    {
        var validation = ValidateSelection(selectedLineIds);
        if (validation != null) return validation;
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var lines = await db.SalesQuoteLines.Include(x => x.SalesQuote)
            .Where(x => selectedLineIds.Contains(x.Id) && x.SalesQuote!.CompanyId == companyId).ToListAsync();
        if (lines.Count != selectedLineIds.Count) return "A selected quote line was not found in this company.";
        var dimensionError = await DimensionService.ValidateAsync(db, companyId, lines.Select(x => x.DimensionSetId), true);
        if (dimensionError.Length > 0) return dimensionError;
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || (!x.ItemId.HasValue && string.IsNullOrWhiteSpace(x.Description)))) return "Selected quote lines need an item or description, positive quantities and non-negative prices.";
        if (lines.Any(x => x.SalesQuote == null || x.SalesQuote.Status is SalesQuoteStatus.Cancelled or SalesQuoteStatus.Expired or SalesQuoteStatus.Converted))
            return "Cancelled, expired or fully converted quotes cannot be converted again.";
        var alreadyOrderedQuoteLineIds = await db.SalesOrderLines
            .Where(x => x.SalesQuoteLineId.HasValue && selectedLineIds.Contains(x.SalesQuoteLineId.Value))
            .Select(x => x.SalesQuoteLineId!.Value).ToListAsync();
        if (alreadyOrderedQuoteLineIds.Count > 0) return "One or more selected quote lines already belong to a sales order.";
        if (lines.Any(x => !x.SalesQuote!.WarehouseId.HasValue || x.SalesQuote.CurrencyId == Guid.Empty))
            return "Every quote must have a site and currency before it can be converted.";
        if (lines.Any(x => x.SalesQuote!.ValidUntil.HasValue && x.SalesQuote.ValidUntil < DateOnly.FromDateTime(DateTime.Today)))
            return "Expired quote lines cannot be converted.";

        var siteIds = lines.Select(x => x.SalesQuote!.WarehouseId!.Value).Distinct().ToList();
        if (await db.Warehouses.CountAsync(x => siteIds.Contains(x.Id) && x.CompanyId == companyId && (x.Type == WarehouseType.Sales || x.Type == WarehouseType.Both)) != siteIds.Count)
            return "A source site is no longer available for sales.";
        var itemIds = lines.Where(x => x.ItemId.HasValue).Select(x => x.ItemId!.Value).Distinct().ToList();
        var itemMap = await db.Items.Include(x => x.UomConversions).AsNoTracking().Where(x => x.CompanyId == companyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        if (itemMap.Count != itemIds.Count) return "A selected quote item is unavailable for this company.";
        var uomError = await ValidateSourceUomsAsync(db, companyId, lines.Where(x => x.ItemId.HasValue).Select(x => (x.ItemId!.Value, x.UomId, x.UomConversionFactor)).ToList());
        if (uomError != null) return uomError;
        foreach (var demand in lines.Where(x => x.ItemId.HasValue && !itemMap[x.ItemId.Value].IsService).GroupBy(x => (ItemId: x.ItemId!.Value, SiteId: x.SalesQuote!.WarehouseId!.Value)))
        {
            var available = await _inventory.GetAvailableToPromiseAsync(companyId, demand.Key.ItemId, demand.Key.SiteId);
            if (demand.Sum(x => x.Quantity) > available)
                return $"Cannot reserve {demand.Sum(x => x.Quantity):G29} of {itemMap[demand.Key.ItemId].Name}. Only {available:G29} available.";
        }

        foreach (var group in lines.GroupBy(x => new
        {
            x.SalesQuote!.CompanyId,
            x.SalesQuote.CustomerId,
            WarehouseId = x.SalesQuote.WarehouseId!.Value,
            x.SalesQuote.CurrencyId
        }))
        {
            var quotes = group.Select(x => x.SalesQuote!).DistinctBy(x => x.Id).ToList();
            if (destinationTerms == null && !HasCompatibleSalesTerms(quotes)) return "Selected quotes share customer, site and currency but have different tax, discount or withholding settings. Convert compatible lines together.";
            var first = quotes[0];
            var (termsError, selectedTerms, selectedTax, selectedWithholding) = await ResolveTermsAsync(db, companyId, destinationTerms, group.Key.CustomerId, group.Key.WarehouseId, group.Key.CurrencyId);
            if (termsError != null) return termsError;
            if (selectedWithholding != null && (!selectedWithholding.SalesGlAccountId.HasValue || selectedWithholding.SalesGlAccountId == Guid.Empty || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == selectedWithholding.SalesGlAccountId && x.CompanyId == companyId && x.IsActive)))
                return "The selected withholding tax needs an active sales receivable account.";
            var order = new SalesOrder
            {
                Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = group.Key.CustomerId,
                WarehouseId = group.Key.WarehouseId, CurrencyId = group.Key.CurrencyId,
                OrderNumber = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AR_Order),
                Date = DateOnly.FromDateTime(DateTime.Today), TransactionDateTime = DateTime.Now,
                Status = OrderStatus.Order, ProjectId = first.ProjectId, ExchangeRate = first.ExchangeRate,
                TaxId = first.TaxId, TaxGLAccountId = first.TaxGlAccountId,
                DiscountPercentage = first.DiscountPercentage, DiscountAmount = first.DiscountAmount,
                DiscountGlAccountId = first.DiscountGlAccountId,
                WithholdingTaxId = first.WithholdingTaxId, WithholdingTaxName = first.WithholdingTaxName,
                WithholdingPercentage = first.WithholdingPercentage, WithholdingSalesGlAccountId = first.WithholdingSalesGlAccountId
            };
            if (selectedTerms != null)
            {
                order.ExchangeRate = selectedTerms.ExchangeRate;
                order.TaxId = selectedTax?.Id;
                order.TaxGLAccountId = selectedTax?.GLAccountId;
                order.DiscountPercentage = selectedTerms.DiscountPercentage;
                order.DiscountAmount = selectedTerms.DiscountAmount;
                order.WithholdingTaxId = selectedWithholding?.Id;
                order.WithholdingTaxName = selectedWithholding?.Name;
                order.WithholdingPercentage = selectedWithholding?.PercentageValue ?? 0;
                order.WithholdingSalesGlAccountId = selectedWithholding?.SalesGlAccountId;
            }
            foreach (var source in group)
            {
                order.Lines.Add(new SalesOrderLine
                { DimensionSetId = source.DimensionSetId,
                    Id = Guid.NewGuid(), HeaderId = order.Id, SalesQuoteLineId = source.Id,
                    ItemId = source.ItemId, Description = source.Description, Quantity = source.Quantity,
                    UnitPrice = source.UnitPrice, UomId = source.UomId, UomName = source.UomName,
                    UomConversionFactor = source.UomConversionFactor
                });
            }
            db.SalesOrders.Add(order);
            foreach (var quote in quotes)
            {
                var quoteLines = await db.SalesQuoteLines.Where(x => x.SalesQuoteId == quote.Id).ToListAsync();
                var selectedQuoteIds = group.Where(x => x.SalesQuoteId == quote.Id).Select(x => x.Id).ToHashSet();
                var mappedIds = await db.SalesOrderLines.Where(x => x.SalesQuoteLineId.HasValue)
                    .Select(x => x.SalesQuoteLineId!.Value).ToListAsync();
                bool fullyConverted = quoteLines.All(x => mappedIds.Contains(x.Id) || selectedQuoteIds.Contains(x.Id));
                quote.Status = fullyConverted ? SalesQuoteStatus.Converted : SalesQuoteStatus.PartiallyConverted;
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return string.Empty;
    }

    /// <summary>Creates one normal purchase invoice from manual item lines and/or posted receipt lines.</summary>
    public async Task<string> CreateNormalPurchaseInvoiceDraftAsync(Guid companyId, Guid vendorId, Guid currencyId,
        IReadOnlyCollection<Guid> receiptLineIds, IReadOnlyCollection<ManualPurchaseInvoiceLineInput> manualLines, DestinationDocumentTerms terms, Guid? existingInvoiceId = null, Func<AppDbContext, Guid, Task<string>>? submit = null)
    {
        if (companyId == Guid.Empty || vendorId == Guid.Empty || currencyId == Guid.Empty) return "Select a supplier and currency.";
        if (receiptLineIds.Count == 0 && manualLines.Count == 0) return "Add at least one invoice line.";
        if (receiptLineIds.Count > 0 && manualLines.Count > 0) return "Choose either goods receipt lines or manual item lines for one invoice. They cannot be combined.";
        if (receiptLineIds.Any(x => x == Guid.Empty) || receiptLineIds.Distinct().Count() != receiptLineIds.Count) return "A receipt line cannot be selected more than once.";
        if (manualLines.Any(x => x.ItemId == Guid.Empty || x.Quantity <= 0 || x.UnitCost < 0)) return "Every manual line needs an item, positive quantity and non-negative unit cost.";
        if (!terms.InvoiceDate.HasValue) return "Enter an invoice date.";
        if (terms.PartnerId != vendorId || terms.CurrencyId != currencyId || terms.SiteId != Guid.Empty || !terms.Confirmed) return "Review and confirm the invoice terms for the selected supplier and currency.";
        if (terms.ExchangeRate <= 0 || terms.DiscountPercentage < 0 || terms.DiscountPercentage > 100 || terms.DiscountAmount < 0 || (terms.DiscountPercentage > 0 && terms.DiscountAmount > 0)) return "Enter a positive exchange rate and valid discount terms.";
        string number = "";
        try { if (!existingInvoiceId.HasValue) number = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AP_Invoice, terms.InvoiceDate); }
        catch (Microsoft.Data.SqlClient.SqlException) { return "An invoice number could not be reserved because the database is busy. Try again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "An invoice number could not be reserved because the database is busy. Try again."; }
        catch (InvalidOperationException) { return "An invoice number could not be reserved. Check the purchase invoice numbering setup."; }
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            PurchaseInvoice? existingInvoice = null;
            if (existingInvoiceId.HasValue)
            {
                existingInvoice = await db.PurchaseInvoices
                    .FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {existingInvoiceId.Value} AND [CompanyId] = {companyId}")
                    .Include(x => x.Lines).FirstOrDefaultAsync();
                if (existingInvoice == null || existingInvoice.IsDirectBill || existingInvoice.Lines.Any(x => x.GoodsReceiptLineId.HasValue) || receiptLineIds.Count > 0)
                    return "Only manual item invoices can be edited.";
                var batch = existingInvoice.GLBatchId.HasValue ? await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == existingInvoice.GLBatchId && x.CompanyId == companyId) : null;
                if (batch?.Status == BatchStatus.Posted || (existingInvoice.IsPosted && batch == null)) return "Posted or cancelled invoices cannot be edited.";
                if (existingInvoice.GLBatchId.HasValue && submit == null) return "Use Update Staged Invoice to refresh this invoice and its review batch together.";
                number = existingInvoice.PurchaseInvoiceNumber;
            }
            var routeError = await InvoiceRoutePolicy.ValidateAsync(db, companyId, TransactionTemplateModule.AP, terms.Route);
            if (routeError != null) return routeError;

            var vendor = await db.Vendors.FirstOrDefaultAsync(x => x.Id == vendorId && x.CompanyId == companyId);
            if (vendor == null || !await db.Currencies.AnyAsync(x => x.Id == currencyId && x.CompanyId == companyId)) return "The selected supplier or currency is unavailable in this company.";
            if (receiptLineIds.Count == 0 && vendor.CurrencyId != currencyId) return "Invoice currency is inherited from the supplier and cannot be changed.";
            var (termsError, resolved, tax, withholding) = await ResolveTermsAsync(db, companyId, new[] { terms }, vendorId, Guid.Empty, currencyId);
            if (termsError != null) return termsError;
            if (withholding != null && (withholding.WithholdingGlAccountId == Guid.Empty || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == withholding.WithholdingGlAccountId && x.CompanyId == companyId && x.IsActive))) return "The selected withholding tax needs an active purchase GL account.";
            var reference = PurchaseInvoiceReference.Resolve(number, terms.SupplierInvoiceNumber,
                existingInvoice?.SupplierInvoiceNumber, existingInvoice?.ExternalInvoiceNumber);
            if (!string.IsNullOrWhiteSpace(reference))
            {
                var key = reference.ToUpperInvariant();
                if (await db.PurchaseInvoices.AnyAsync(x => x.Id != existingInvoiceId && x.CompanyId == companyId && x.VendorId == vendorId && ((x.SupplierInvoiceNumber != null && x.SupplierInvoiceNumber.ToUpper() == key) || (x.ExternalInvoiceNumber != null && x.ExternalInvoiceNumber.ToUpper() == key)))) return "That supplier invoice number is already registered for this supplier.";
            }
            var receiptLines = await db.GoodsReceiptLines.Where(x => receiptLineIds.Contains(x.Id)).ToListAsync();
            if (receiptLines.Count != receiptLineIds.Count) return "A selected receipt line no longer exists.";
            if (receiptLines.Any(x => x.PurchaseInvoiceLineId.HasValue)) return "Invoice-sourced receipts cannot be invoiced again.";
            receiptLines = receiptLineIds.Select(id => receiptLines.Single(x => x.Id == id)).ToList();
            if (await db.PurchaseInvoiceLines.AnyAsync(x => x.GoodsReceiptLineId.HasValue && receiptLineIds.Contains(x.GoodsReceiptLineId.Value))) return "A selected receipt line is already reserved by an invoice. Refresh the available lines.";
            if (receiptLines.Any(x => !x.PurchaseOrderLineId.HasValue)) return "A selected receipt has an unresolved purchase order link.";
            var receiptIds = receiptLines.Select(x => x.GoodsReceiptId).Distinct().ToList();
            var receipts = await db.GoodsReceipts.Where(x => receiptIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            if (receipts.Count != receiptIds.Count || receipts.Values.Any(x => !x.GLBatchId.HasValue)) return "Only submitted goods receipts can be invoiced.";
            var batchIds = receipts.Values.Select(x => x.GLBatchId!.Value).Distinct().ToList();
            if (await db.GLBatches.CountAsync(x => batchIds.Contains(x.Id) && x.CompanyId == companyId && x.Status == BatchStatus.Posted) != batchIds.Count) return "Every source receipt must be posted to the General Ledger first.";
            var poLineIds = receiptLines.Select(x => x.PurchaseOrderLineId!.Value).Distinct().ToList();
            var poLines = await db.PurchaseOrderLines.Where(x => poLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var poIds = poLines.Values.Select(x => x.PurchaseOrderId).Distinct().ToList();
            var orders = await db.PurchaseOrders.Where(x => poIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            if (poLines.Count != poLineIds.Count || orders.Count != poIds.Count || orders.Values.Any(x => x.VendorId != vendorId || x.CurrencyId != currencyId) || receiptLines.Any(x => x.QuantityReceived <= 0 || !poLines.ContainsKey(x.PurchaseOrderLineId!.Value))) return "Each receipt line must belong to the selected supplier and currency and have a valid purchase order.";
            var priorBilled = await db.PurchaseInvoiceLines.AsNoTracking()
                .Where(x => x.PurchaseOrderLineId.HasValue && poLineIds.Contains(x.PurchaseOrderLineId.Value))
                .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityBilled) })
                .ToDictionaryAsync(x => x.Id, x => x.Qty);
            foreach (var group in receiptLines.GroupBy(x => x.PurchaseOrderLineId!.Value))
            {
                var available = Math.Max(0m, poLines[group.Key].QuantityOrdered - priorBilled.GetValueOrDefault(group.Key));
                if (group.Sum(x => x.QuantityReceived) > available)
                    return "The selected receipt quantities exceed the uninvoiced order balance. Refresh the source lines.";
            }
            var itemIds = manualLines.Select(x => x.ItemId).Concat(poLines.Values.Select(x => x.ItemId)).Distinct().ToList();
            var items = await db.Items.Include(x => x.UomConversions).Where(x => itemIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            if (items.Count != itemIds.Count) return "An invoice item is unavailable in this company.";
            var uoms = await db.UnitOfMeasures.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToHashSetAsync();
            var rules = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var itemFactors = await db.ItemUomConversionLines.AsNoTracking().Where(x => x.IsActive && itemIds.Contains(x.ItemId)).GroupBy(x => new { x.ItemId, x.UomId }).ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
            foreach (var line in manualLines)
            {
                var item = items[line.ItemId]; var factors = itemFactors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                if (line.UomId.HasValue && (!uoms.Contains(line.UomId.Value) || (line.UomId != item.UomId && !factors.ContainsKey(line.UomId.Value) && !UomConversion.IsConfigured(item, line.UomId)))) return $"The selected unit for {item.Name} is not configured.";
                if (line.UomConversionFactor <= 0 || Math.Abs(UomConversion.FactorFor(item, line.UomId, factors, rules) - line.UomConversionFactor) > 0.000000000000001m) return $"The unit conversion for {item.Name} has changed. Refresh the form.";
            }
            var invoice = new PurchaseInvoice
            {
                CompanyId = companyId, VendorId = vendorId, CurrencyId = currencyId, PurchaseInvoiceNumber = number,
                SupplierInvoiceNumber = reference, ExternalInvoiceNumber = reference, BillDate = terms.InvoiceDate.Value.Date, InvoiceDate = terms.InvoiceDate.Value.Date,
                IsDirectBill = false, ExchangeRate = resolved!.ExchangeRate, AccountsPayableGlId = vendor.PayablesAccountId ?? Guid.Empty,
                WarehouseId = receipts.Count > 0 && receipts.Values.Select(x => x.WarehouseId).Distinct().Count() == 1 ? receipts.Values.First().WarehouseId : null,
                PurchaseOrderId = orders.Count == 1 ? orders.Keys.First() : null,
                ProjectId = orders.Count > 0 && orders.Values.Select(x => x.ProjectId).Distinct().Count() == 1 ? orders.Values.First().ProjectId : null,
                TaxId = tax?.Id, TaxGLAccountId = tax?.GLAccountId, DiscountPercentage = resolved.DiscountPercentage, DiscountAmount = resolved.DiscountAmount,
                WithholdingTaxId = withholding?.Id, WithholdingTaxName = withholding?.Name, WithholdingPercentage = withholding?.PercentageValue ?? 0, WithholdingGlAccountId = withholding?.WithholdingGlAccountId,
                DiscountGlAccountId = orders.Count > 0 ? orders.Values.First().DiscountGlAccountId : null,
                GoodsReceiptClearingGlAccountId = orders.Count > 0 ? orders.Values.First().GoodsReceiptClearingGlAccountId : null
            };
            foreach (var source in receiptLines)
            {
                var poLine = poLines[source.PurchaseOrderLineId!.Value]; var receipt = receipts[source.GoodsReceiptId];
                invoice.Lines.Add(new PurchaseInvoiceLine { DimensionSetId = terms.LineDimensionSets.GetValueOrDefault(source.Id, source.DimensionSetId), PurchaseInvoiceId = invoice.Id, GoodsReceiptLineId = source.Id, PurchaseOrderLineId = poLine.Id,
                    ItemId = poLine.ItemId, Description = items[poLine.ItemId].Name, QuantityBilled = source.QuantityReceived, UnitCostBilled = poLine.UnitCost,
                    UomId = source.UomId ?? poLine.UomId, UomName = source.UomName, UomConversionFactor = source.UomConversionFactor, ExpenseGlAccountId = receipt.InventoryGlAccountId });
            }
            if (terms.InvoiceFullOrderQuantity)
            {
                foreach (var group in invoice.Lines.Where(x => x.PurchaseOrderLineId.HasValue).GroupBy(x => x.PurchaseOrderLineId!.Value))
                {
                    var remaining = poLines[group.Key].QuantityOrdered - priorBilled.GetValueOrDefault(group.Key);
                    group.First().QuantityBilled += remaining - group.Sum(x => x.QuantityBilled);
                }
            }
            foreach (var line in manualLines)
            {
                var item = items[line.ItemId];
                invoice.Lines.Add(new PurchaseInvoiceLine { DimensionSetId = line.DimensionSetId,  PurchaseInvoiceId = invoice.Id, ItemId = item.Id, Description = item.Name,
                    UomId = line.UomId, UomName = string.IsNullOrWhiteSpace(line.UomName) ? item.UoM : line.UomName.Trim(), UomConversionFactor = line.UomConversionFactor,
                    QuantityBilled = UomConversion.ToBase(line.Quantity, line.UomConversionFactor), UnitCostBilled = UomConversion.PriceToBase(line.UnitCost, line.UomConversionFactor),
                    ExpenseGlAccountId = item.IsService ? item.CostOfGoodsSoldAccountId : Guid.Empty });
            }
            var selectionError = await DimensionService.ValidateAsync(db, companyId, invoice.Lines.Select(x => x.DimensionSetId), false);
            if (selectionError.Length > 0) return selectionError;
            invoice.CustomTransactionTypeId = terms.Route.TemplateId;
            invoice.TaxGLAccountId = terms.Route.TaxAccountId ?? invoice.TaxGLAccountId;
            invoice.DiscountGlAccountId = terms.Route.DiscountAccountId ?? invoice.DiscountGlAccountId;
            invoice.OverrideExpenseGlAccountId = terms.Route.DebitAccountId;
            invoice.OverrideAccountsPayableGlAccountId = terms.Route.CreditAccountId;
            var gross = invoice.Lines.Sum(x => x.LineTotal); var discount = invoice.DiscountPercentage > 0 ? gross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, gross);
            var taxable = gross - discount; invoice.TotalAmountForeign = Math.Round(taxable + taxable * (tax?.Per ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);
            invoice.TotalAmount = Math.Round(invoice.TotalAmountForeign * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            invoice.WithholdingAmountForeign = Math.Round(taxable * invoice.WithholdingPercentage / 100m, 4, MidpointRounding.AwayFromZero);
            invoice.WithholdingAmount = Math.Round(invoice.WithholdingAmountForeign * invoice.ExchangeRate, 4, MidpointRounding.AwayFromZero);
            if (existingInvoice != null)
            {
                invoice.Id = existingInvoice.Id; invoice.GLBatchId = existingInvoice.GLBatchId;
                invoice.IsPosted = existingInvoice.IsPosted; invoice.PostedDate = existingInvoice.PostedDate;
                db.PurchaseInvoiceLines.RemoveRange(existingInvoice.Lines);
                existingInvoice.Lines.Clear();
                db.Entry(existingInvoice).CurrentValues.SetValues(invoice);
                foreach (var row in invoice.Lines) { row.PurchaseInvoiceId = existingInvoice.Id; db.PurchaseInvoiceLines.Add(row); }
            }
            else db.PurchaseInvoices.Add(invoice);
            await db.SaveChangesAsync();
            if (submit != null)
            {
                var error = await submit(db, invoice.Id);
                if (!string.IsNullOrEmpty(error)) return error;
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync(); terms.SavedInvoiceId = invoice.Id; return string.Empty;
        }
        catch (DbUpdateException) { return "The invoice could not be saved. Refresh the source lines and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice could not be saved because the database is busy. Refresh and try again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "The invoice could not be saved because another transaction is using its source lines. Refresh and try again."; }
    }

    /// <summary>Creates draft purchase invoices from posted receipt lines, one invoice per supplier/site/currency.</summary>
    public async Task<string> CreatePurchaseInvoicesFromReceiptLinesAsync(Guid companyId, IReadOnlyCollection<Guid> selectedLineIds, IReadOnlyCollection<DestinationDocumentTerms>? destinationTerms = null)
    {
        var validation = ValidateSelection(selectedLineIds);
        if (validation != null) return validation;
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var receiptLines = await db.GoodsReceiptLines.Where(x => selectedLineIds.Contains(x.Id)).ToListAsync();
        if (receiptLines.Count != selectedLineIds.Count) return "A selected receipt line was not found.";
        if (receiptLines.Any(x => x.PurchaseInvoiceLineId.HasValue)) return "Invoice-sourced receipts cannot be invoiced again.";
        if (await db.PurchaseInvoiceLines.AnyAsync(x => x.GoodsReceiptLineId.HasValue && selectedLineIds.Contains(x.GoodsReceiptLineId.Value)))
            return "One or more selected receipt lines have already been invoiced.";

        var receipts = await db.GoodsReceipts.Where(x => receiptLines.Select(l => l.GoodsReceiptId).Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        if (receiptLines.Any(x => !x.PurchaseOrderLineId.HasValue))
            return "A legacy goods receipt has an unresolved purchase order link. Restore its source before invoicing.";
        var poLineIds = receiptLines.Select(x => x.PurchaseOrderLineId!.Value).Distinct().ToList();
        var poLines = await db.PurchaseOrderLines.Where(x => poLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var orderIds = poLines.Values.Select(x => x.PurchaseOrderId).Distinct().ToList();
        var orders = await db.PurchaseOrders.Where(x => orderIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
        if (orders.Count != orderIds.Count) return "One or more receipt lines do not belong to a purchase order in this company.";
        if (receiptLines.Any(x => !receipts.TryGetValue(x.GoodsReceiptId, out var receipt) || receipt.CompanyId != companyId || !receipt.GLBatchId.HasValue))
            return "Only submitted goods receipts can be invoiced.";
        var receiptBatchIds = receipts.Values.Select(x => x.GLBatchId!.Value).Distinct().ToList();
        var postedBatchIds = await db.GLBatches.Where(x => receiptBatchIds.Contains(x.Id) && x.CompanyId == companyId && x.Status == BatchStatus.Posted)
            .Select(x => x.Id).ToListAsync();
        if (receiptLines.Any(x => !postedBatchIds.Contains(receipts[x.GoodsReceiptId].GLBatchId!.Value)))
            return "Goods receipts must be posted to the ledger before they can be invoiced.";
        if (receiptLines.Any(x => x.QuantityReceived <= 0 || !poLines.ContainsKey(x.PurchaseOrderLineId!.Value)))
            return "Every selected receipt line must have a positive received quantity and a valid purchase order line.";
        var priorQuantities = await db.PurchaseInvoiceLines.Where(x => x.PurchaseOrderLineId.HasValue && poLineIds.Contains(x.PurchaseOrderLineId.Value))
            .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityBilled) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
        if (receiptLines.GroupBy(x => x.PurchaseOrderLineId!.Value).Any(x => x.Sum(y => y.QuantityReceived) + priorQuantities.GetValueOrDefault(x.Key) > poLines[x.Key].QuantityOrdered))
            return "Selected receipts exceed the uninvoiced order quantity.";
        if (receipts.Values.Any(x => !x.WarehouseId.HasValue)) return "All source goods receipts need a warehouse before invoice grouping.";

        var itemIds = poLines.Values.Select(x => x.ItemId).Distinct().ToList();
        var itemNames = await db.Items.AsNoTracking().Where(x => x.CompanyId == companyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        if (itemNames.Count != itemIds.Count) return "An invoice item is unavailable in this company.";

        var supplierReferences = new HashSet<(Guid VendorId, string Reference)>();
        foreach (var group in receiptLines.GroupBy(x =>
        {
            var po = orders[poLines[x.PurchaseOrderLineId!.Value].PurchaseOrderId];
            return (po.CompanyId, po.VendorId, po.CurrencyId);
        }))
        {
            var groupOrders = group.Select(x => orders[poLines[x.PurchaseOrderLineId!.Value].PurchaseOrderId]).DistinctBy(x => x.Id).ToList();
            if (destinationTerms == null && !HasCompatiblePurchaseOrderTerms(groupOrders)) return "Selected purchase orders share supplier, site and currency but have incompatible tax, discount or withholding settings.";
            var first = groupOrders[0];
            var (termsError, selectedTerms, selectedTax, selectedWithholding) = await ResolveTermsAsync(db, companyId, destinationTerms, group.Key.VendorId, Guid.Empty, group.Key.CurrencyId);
            if (termsError != null) return termsError;
            if (selectedWithholding != null && (selectedWithholding.WithholdingGlAccountId == Guid.Empty || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == selectedWithholding.WithholdingGlAccountId && x.CompanyId == companyId && x.IsActive)))
                return "The selected withholding tax needs an active purchase GL account.";
            var number = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AP_Invoice, selectedTerms?.InvoiceDate);
            var supplierReference = PurchaseInvoiceReference.Resolve(number, selectedTerms?.SupplierInvoiceNumber);
            if (!string.IsNullOrWhiteSpace(supplierReference))
            {
                var referenceKey = (group.Key.VendorId, supplierReference.ToUpperInvariant());
                if (!supplierReferences.Add(referenceKey) || await db.PurchaseInvoices.AnyAsync(x => x.CompanyId == companyId && x.VendorId == group.Key.VendorId && ((x.SupplierInvoiceNumber != null && x.SupplierInvoiceNumber.ToUpper() == referenceKey.Item2) || (x.ExternalInvoiceNumber != null && x.ExternalInvoiceNumber.ToUpper() == referenceKey.Item2))))
                    return "That supplier invoice number is already registered or used by another selected group for this supplier.";
            }
            var vendor = await db.Vendors.FirstAsync(x => x.Id == group.Key.VendorId && x.CompanyId == companyId);
            var invoice = new PurchaseInvoice
            {
                Id = Guid.NewGuid(), CompanyId = companyId, VendorId = group.Key.VendorId,
                WarehouseId = group.Select(x => receipts[x.GoodsReceiptId].WarehouseId).Distinct().Count() == 1 ? receipts[group.First().GoodsReceiptId].WarehouseId : null, CurrencyId = group.Key.CurrencyId,
                PurchaseOrderId = groupOrders.Count == 1 ? groupOrders[0].Id : null,
                PurchaseInvoiceNumber = number,
                ExternalInvoiceNumber = supplierReference, SupplierInvoiceNumber = supplierReference,
                BillDate = selectedTerms?.InvoiceDate?.Date ?? DateTime.Today, InvoiceDate = selectedTerms?.InvoiceDate?.Date ?? DateTime.Today,
                AccountsPayableGlId = vendor.PayablesAccountId ?? Guid.Empty,
                ProjectId = first.ProjectId, ExchangeRate = first.ExchangeRate,
                TaxId = first.TaxId, TaxGLAccountId = first.TaxGLAccountId,
                DiscountPercentage = first.DiscountPercentage, DiscountAmount = first.DiscountAmount,
                DiscountGlAccountId = first.DiscountGlAccountId, GoodsReceiptClearingGlAccountId = first.GoodsReceiptClearingGlAccountId,
                WithholdingTaxId = first.WithholdingTaxId, WithholdingTaxName = first.WithholdingTaxName,
                WithholdingPercentage = first.WithholdingPercentage, WithholdingAmountForeign = first.WithholdingAmountForeign,
                WithholdingAmount = first.WithholdingAmount, WithholdingGlAccountId = first.WithholdingGlAccountId
            };
            if (selectedTerms != null)
            {
                invoice.ExchangeRate = selectedTerms.ExchangeRate;
                invoice.TaxId = selectedTax?.Id;
                invoice.TaxGLAccountId = selectedTax?.GLAccountId;
                invoice.DiscountPercentage = selectedTerms.DiscountPercentage;
                invoice.DiscountAmount = selectedTerms.DiscountAmount;
                invoice.WithholdingTaxId = selectedWithholding?.Id;
                invoice.WithholdingTaxName = selectedWithholding?.Name;
                invoice.WithholdingPercentage = selectedWithholding?.PercentageValue ?? 0;
                invoice.WithholdingGlAccountId = selectedWithholding?.WithholdingGlAccountId;
            }
            foreach (var receiptLine in group)
            {
                var poLine = poLines[receiptLine.PurchaseOrderLineId!.Value];
                var receipt = receipts[receiptLine.GoodsReceiptId];
                invoice.Lines.Add(new PurchaseInvoiceLine
                { DimensionSetId = receiptLine.DimensionSetId,
                    Id = Guid.NewGuid(), PurchaseInvoiceId = invoice.Id, GoodsReceiptLineId = receiptLine.Id,
                    PurchaseOrderLineId = poLine.Id, ItemId = poLine.ItemId,
                    QuantityBilled = receiptLine.QuantityReceived, UnitCostBilled = poLine.UnitCost,
                    UomId = receiptLine.UomId ?? poLine.UomId, UomName = receiptLine.UomName,
                    UomConversionFactor = receiptLine.UomConversionFactor,
                    ExpenseGlAccountId = receipt.InventoryGlAccountId,
                    Description = itemNames[poLine.ItemId]
                });
            }
            var gross = invoice.Lines.Sum(x => x.LineTotal);
            var discount = invoice.DiscountPercentage > 0 ? gross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, gross);
            var taxRate = invoice.TaxId.HasValue ? (await db.Taxes.FirstOrDefaultAsync(x => x.Id == invoice.TaxId.Value && x.CompanyId == companyId))?.Per ?? 0m : 0m;
            var taxable = gross - discount;
            invoice.TotalAmountForeign = Math.Round(taxable + taxable * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
            invoice.TotalAmount = Math.Round(invoice.TotalAmountForeign * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            invoice.WithholdingAmountForeign = Math.Round(taxable * invoice.WithholdingPercentage / 100m, 4, MidpointRounding.AwayFromZero);
            invoice.WithholdingAmount = Math.Round(invoice.WithholdingAmountForeign * invoice.ExchangeRate, 4, MidpointRounding.AwayFromZero);
            db.PurchaseInvoices.Add(invoice);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return string.Empty;
    }

    /// <summary>Creates one normal sales invoice from manual item lines and/or posted shipment lines.</summary>
    public async Task<string> CreateNormalSalesInvoiceDraftAsync(Guid companyId, Guid customerId, Guid currencyId,
        IReadOnlyCollection<Guid> shipmentLineIds, IReadOnlyCollection<ManualSalesInvoiceLineInput> manualLines, DestinationDocumentTerms terms, Guid? existingInvoiceId = null, Func<AppDbContext, Guid, Task<string>>? submit = null)
    {
        if (companyId == Guid.Empty || customerId == Guid.Empty || currencyId == Guid.Empty) return "Select a customer and currency.";
        if (shipmentLineIds.Count == 0 && manualLines.Count == 0) return "Add at least one invoice line.";
        if (shipmentLineIds.Count > 0 && manualLines.Count > 0) return "Choose either shipment lines or manual item lines for one invoice. They cannot be combined.";
        if (shipmentLineIds.Any(x => x == Guid.Empty) || shipmentLineIds.Distinct().Count() != shipmentLineIds.Count)
            return "A shipment line cannot be selected more than once.";
        if (manualLines.Any(x => x.ItemId == Guid.Empty || x.Quantity <= 0 || x.UnitPrice < 0))
            return "Every manual line needs an item, positive quantity and non-negative unit price.";
        if (!terms.InvoiceDate.HasValue) return "Enter an invoice date.";
        if (terms.PartnerId != customerId || terms.CurrencyId != currencyId || terms.SiteId != Guid.Empty || !terms.Confirmed)
            return "Review and confirm the invoice terms for the selected customer and currency.";
        if (terms.ExchangeRate <= 0 || terms.DiscountPercentage < 0 || terms.DiscountPercentage > 100 || terms.DiscountAmount < 0
            || (terms.DiscountPercentage > 0 && terms.DiscountAmount > 0))
            return "Enter a positive exchange rate and valid discount terms.";

        // Reserve the number outside the balance transaction; failed drafts may leave a sequence gap.
        string number = "";
        try { if (!existingInvoiceId.HasValue) number = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AR_Invoice, terms.InvoiceDate); }
        catch (Microsoft.Data.SqlClient.SqlException) { return "An invoice number could not be reserved because the database is busy. Try again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex)) { return "An invoice number could not be reserved because the database is busy. Try again."; }
        catch (InvalidOperationException) { return "An invoice number could not be reserved. Check the sales invoice numbering setup."; }
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            SalesInvoice? existingInvoice = null;
            if (existingInvoiceId.HasValue)
            {
                existingInvoice = await db.SalesInvoices
                    .FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {existingInvoiceId.Value} AND [CompanyId] = {companyId}")
                    .Include(x => x.Lines).FirstOrDefaultAsync();
                if (existingInvoice == null || existingInvoice.IsDirectInvoice || existingInvoice.Lines.Any(x => x.ShipmentLineId.HasValue) || shipmentLineIds.Count > 0)
                    return "Only manual item invoices can be edited.";
                var batch = existingInvoice.GLBatchId.HasValue ? await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == existingInvoice.GLBatchId && x.CompanyId == companyId) : null;
                if (batch?.Status == BatchStatus.Posted || existingInvoice.Status is SalesInvoiceStatus.Posted or SalesInvoiceStatus.Cancelled) return "Posted or cancelled invoices cannot be edited.";
                if (existingInvoice.GLBatchId.HasValue && submit == null) return "Use Update Staged Invoice to refresh this invoice and its review batch together.";
                number = existingInvoice.InvoiceNumber;
            }
            var routeError = await InvoiceRoutePolicy.ValidateAsync(db, companyId, TransactionTemplateModule.AR, terms.Route);
            if (routeError != null) return routeError;

            var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId && x.CompanyId == companyId);
            if (customer == null || !await db.Currencies.AnyAsync(x => x.Id == currencyId && x.CompanyId == companyId))
                return "The selected customer or currency is unavailable in this company.";
            if (shipmentLineIds.Count == 0 && customer.CurrencyId != currencyId) return "Invoice currency is inherited from the customer and cannot be changed.";
            var (termsError, resolved, tax, withholding) = await ResolveTermsAsync(db, companyId, new[] { terms }, customerId, Guid.Empty, currencyId);
            if (termsError != null) return termsError;
            if (withholding != null && (!withholding.SalesGlAccountId.HasValue || withholding.SalesGlAccountId == Guid.Empty
                || !await db.SegChartOfAccounts.AnyAsync(x => x.Id == withholding.SalesGlAccountId && x.CompanyId == companyId && x.IsActive)))
                return "The selected withholding tax needs an active sales receivable account.";
            var shipmentLines = await db.SalesShipmentLines.Where(x => shipmentLineIds.Contains(x.Id)).ToListAsync();
            if (shipmentLines.Count != shipmentLineIds.Count) return "A selected shipment line no longer exists.";
            if (shipmentLines.Any(x => !x.SalesOrderLineId.HasValue || x.SalesInvoiceLineId.HasValue)) return "Invoice-sourced shipments cannot be invoiced again.";
            shipmentLines = shipmentLineIds.Select(id => shipmentLines.Single(x => x.Id == id)).ToList();
            if (await db.SalesInvoiceLines.AnyAsync(x => x.ShipmentLineId.HasValue && shipmentLineIds.Contains(x.ShipmentLineId.Value)))
                return "A selected shipment line is already reserved by an invoice. Refresh the available lines.";
            var shipmentIds = shipmentLines.Select(x => x.ShipmentId).Distinct().ToList();
            var shipments = await db.SalesShipments.Where(x => shipmentIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            if (shipments.Count != shipmentIds.Count || shipments.Values.Any(x => x.Status != ShipmentStatus.Shipped || !x.ShipmentBatchId.HasValue))
                return "Only completed shipments can be invoiced.";
            var batchIds = shipments.Values.Select(x => x.ShipmentBatchId!.Value).Distinct().ToList();
            if (await db.GLBatches.CountAsync(x => batchIds.Contains(x.Id) && x.CompanyId == companyId && x.Status == BatchStatus.Posted) != batchIds.Count)
                return "Every source shipment must be posted to the General Ledger first.";
            var orderLineIds = shipmentLines.Select(x => x.SalesOrderLineId!.Value).Distinct().ToList();
            var orderLines = await db.SalesOrderLines.Include(x => x.Item).Where(x => orderLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var orderIds = orderLines.Values.Select(x => x.HeaderId).Distinct().ToList();
            var orders = await db.SalesOrders.Where(x => orderIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            if (orderLines.Count != orderLineIds.Count || orders.Count != orderIds.Count
                || orders.Values.Any(x => x.CustomerId != customerId || x.CurrencyId != currencyId)
                || orderLines.Values.Any(x => x.Item == null || x.Item.CompanyId != companyId)
                || shipmentLines.Any(x => x.QtyShipped <= 0 || !orderLines.ContainsKey(x.SalesOrderLineId!.Value)
                    || orderLines[x.SalesOrderLineId!.Value].ItemId != x.ItemId))
                return "Each shipment line must belong to the selected customer and currency and match its order item.";
            var priorInvoiced = await db.SalesInvoiceLines.AsNoTracking()
                .Where(x => x.SalesOrderLineId.HasValue && orderLineIds.Contains(x.SalesOrderLineId.Value))
                .GroupBy(x => x.SalesOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityInvoiced) })
                .ToDictionaryAsync(x => x.Id, x => x.Qty);
            foreach (var group in shipmentLines.GroupBy(x => x.SalesOrderLineId!.Value))
            {
                var available = Math.Max(0m, orderLines[group.Key].Quantity - priorInvoiced.GetValueOrDefault(group.Key));
                if (group.Sum(x => x.QtyShipped) > available)
                    return "The selected shipment quantities exceed the uninvoiced order balance. Refresh the source lines.";
            }
            var itemIds = manualLines.Select(x => x.ItemId).Distinct().ToList();
            var items = await db.Items.Include(x => x.UomConversions).Where(x => itemIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            if (items.Count != itemIds.Count) return "A manual invoice item is unavailable in this company.";
            var uoms = await db.UnitOfMeasures.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToHashSetAsync();
            var rules = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
            var itemFactors = await db.ItemUomConversionLines.AsNoTracking().Where(x => x.IsActive && itemIds.Contains(x.ItemId))
                .GroupBy(x => new { x.ItemId, x.UomId }).ToDictionaryAsync(x => (x.Key.ItemId, x.Key.UomId), x => x.First().ConversionFactorToBase);
            foreach (var line in manualLines)
            {
                var item = items[line.ItemId];
                var factors = itemFactors.Where(x => x.Key.ItemId == item.Id).ToDictionary(x => x.Key.UomId, x => x.Value);
                if (line.UomId.HasValue && (!uoms.Contains(line.UomId.Value) || (line.UomId != item.UomId && !factors.ContainsKey(line.UomId.Value)
                    && !UomConversion.IsConfigured(item, line.UomId))))
                    return $"The selected unit for {item.Name} is not configured.";
                var expected = UomConversion.FactorFor(item, line.UomId, factors, rules);
                if (line.UomConversionFactor <= 0 || Math.Abs(expected - line.UomConversionFactor) > 0.000000000000001m)
                    return $"The unit conversion for {item.Name} has changed. Refresh the form.";
            }
            var invoice = new SalesInvoice
            {
                CompanyId = companyId, CustomerId = customerId, CurrencyId = currencyId, InvoiceNumber = number,
                Date = DateOnly.FromDateTime(terms.InvoiceDate.Value), Status = SalesInvoiceStatus.Draft,
                IsDirectInvoice = false, ExchangeRate = resolved!.ExchangeRate,
                WarehouseId = shipments.Count > 0 && shipments.Values.Select(x => x.WarehouseId).Distinct().Count() == 1 ? shipments.Values.First().WarehouseId : null,
                SalesOrderId = orders.Count == 1 ? orders.Keys.First() : null,
                ProjectId = orders.Count > 0 && orders.Values.Select(x => x.ProjectId).Distinct().Count() == 1 ? orders.Values.First().ProjectId : null,
                TaxId = tax?.Id, TaxGLAccountId = tax?.GLAccountId, DiscountPercentage = resolved.DiscountPercentage,
                DiscountAmount = resolved.DiscountAmount, WithholdingTaxId = withholding?.Id, WithholdingTaxName = withholding?.Name,
                WithholdingPercentage = withholding?.PercentageValue ?? 0, WithholdingSalesGlAccountId = withholding?.SalesGlAccountId,
                ReceivablesGlAccountId = customer.ReceivablesAccountId,
                DiscountGlAccountId = orders.Count > 0 ? orders.Values.First().DiscountGlAccountId : null
            };
            foreach (var source in shipmentLines)
            {
                var orderLine = orderLines[source.SalesOrderLineId!.Value];
                invoice.Lines.Add(new SalesInvoiceLine { DimensionSetId = terms.LineDimensionSets.GetValueOrDefault(source.Id, source.DimensionSetId), ShipmentLineId = source.Id, SalesOrderLineId = orderLine.Id,
                    ItemId = source.ItemId, Description = string.IsNullOrWhiteSpace(orderLine.Description) ? orderLine.Item?.Name : orderLine.Description,
                    QuantityInvoiced = source.QtyShipped, UnitPrice = orderLine.UnitPrice, UomId = source.UomId,
                    UomName = source.UomName, UomConversionFactor = source.UomConversionFactor,
                    RevenueGlAccountId = orderLine.Item?.SalesIncomeAccountId, TaxId = invoice.TaxId, TaxGLAccountId = invoice.TaxGLAccountId });
            }
            if (terms.InvoiceFullOrderQuantity)
            {
                foreach (var group in invoice.Lines.Where(x => x.SalesOrderLineId.HasValue).GroupBy(x => x.SalesOrderLineId!.Value))
                {
                    var remaining = orderLines[group.Key].Quantity - priorInvoiced.GetValueOrDefault(group.Key);
                    group.First().QuantityInvoiced += remaining - group.Sum(x => x.QuantityInvoiced);
                }
            }
            foreach (var line in manualLines)
            {
                var item = items[line.ItemId];
                invoice.Lines.Add(new SalesInvoiceLine { DimensionSetId = line.DimensionSetId,  ItemId = item.Id, Description = item.Name, UomId = line.UomId,
                    UomName = string.IsNullOrWhiteSpace(line.UomName) ? item.UoM : line.UomName.Trim(), UomConversionFactor = line.UomConversionFactor,
                    QuantityInvoiced = UomConversion.ToBase(line.Quantity, line.UomConversionFactor),
                    UnitPrice = UomConversion.PriceToBase(line.UnitPrice, line.UomConversionFactor),
                    RevenueGlAccountId = item.SalesIncomeAccountId, TaxId = invoice.TaxId, TaxGLAccountId = invoice.TaxGLAccountId });
            }
            var selectionError = await DimensionService.ValidateAsync(db, companyId, invoice.Lines.Select(x => x.DimensionSetId), false);
            if (selectionError.Length > 0) return selectionError;
            invoice.CustomTransactionTypeId = terms.Route.TemplateId;
            invoice.TaxGLAccountId = terms.Route.TaxAccountId ?? invoice.TaxGLAccountId;
            invoice.DiscountGlAccountId = terms.Route.DiscountAccountId ?? invoice.DiscountGlAccountId;
            invoice.ReceivablesGlAccountId = terms.Route.DebitAccountId;
            invoice.DirectIncomeGlAccountId = terms.Route.CreditAccountId;
            foreach (var row in invoice.Lines) { row.RevenueGlAccountId = terms.Route.CreditAccountId ?? row.RevenueGlAccountId; row.TaxGLAccountId = invoice.TaxGLAccountId; }
            var gross = invoice.Lines.Sum(x => x.Amount);
            var discount = invoice.DiscountPercentage > 0 ? gross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, gross);
            invoice.TotalAmountForeign = gross - discount + (gross - discount) * (tax?.Per ?? 0m) / 100m;
            invoice.TotalAmount = Math.Round(invoice.TotalAmountForeign * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            if (existingInvoice != null)
            {
                invoice.Id = existingInvoice.Id; invoice.GLBatchId = existingInvoice.GLBatchId;
                invoice.InvoiceBatchId = existingInvoice.InvoiceBatchId; invoice.Status = existingInvoice.Status;
                db.SalesInvoiceLines.RemoveRange(existingInvoice.Lines);
                existingInvoice.Lines.Clear();
                db.Entry(existingInvoice).CurrentValues.SetValues(invoice);
                foreach (var row in invoice.Lines) { row.SalesInvoiceId = existingInvoice.Id; db.SalesInvoiceLines.Add(row); }
            }
            else db.SalesInvoices.Add(invoice);
            await db.SaveChangesAsync();
            if (submit != null)
            {
                var error = await submit(db, invoice.Id);
                if (!string.IsNullOrEmpty(error)) return error;
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync(); terms.SavedInvoiceId = invoice.Id; return string.Empty;
        }
        catch (DbUpdateException) { return "The invoice could not be saved. Refresh the source lines and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice could not be saved because the database is busy. Refresh and try again."; }
        catch (InvalidOperationException ex) when (HasTransientSqlError(ex))
        { return "The invoice could not be saved because another transaction is using its source lines. Refresh and try again."; }
    }

    /// <summary>Creates draft sales invoices from posted shipment lines, one invoice per customer/site/currency.</summary>
    public async Task<string> CreateSalesInvoicesFromShipmentLinesAsync(Guid companyId, IReadOnlyCollection<Guid> selectedLineIds, IReadOnlyCollection<DestinationDocumentTerms>? destinationTerms = null)
    {
        var validation = ValidateSelection(selectedLineIds);
        if (validation != null) return validation;
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var shipmentLines = await db.SalesShipmentLines.Where(x => selectedLineIds.Contains(x.Id)).ToListAsync();
        if (shipmentLines.Count != selectedLineIds.Count) return "A selected shipment line was not found.";
        if (shipmentLines.Any(x => !x.SalesOrderLineId.HasValue || x.SalesInvoiceLineId.HasValue)) return "Invoice-sourced shipments cannot be invoiced again.";
        if (await db.SalesInvoiceLines.AnyAsync(x => x.ShipmentLineId.HasValue && selectedLineIds.Contains(x.ShipmentLineId.Value)))
            return "One or more selected shipment lines have already been invoiced.";

        var shipments = await db.SalesShipments.Where(x => shipmentLines.Select(l => l.ShipmentId).Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var orderLineIds = shipmentLines.Select(x => x.SalesOrderLineId!.Value).Distinct().ToList();
        var orderLines = await db.SalesOrderLines.Include(x => x.Item).Where(x => orderLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var orderIds = orderLines.Values.Select(x => x.HeaderId).Distinct().ToList();
        var orders = await db.SalesOrders.Where(x => orderIds.Contains(x.Id) && x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
        if (orders.Count != orderIds.Count) return "One or more shipment lines do not belong to a sales order in this company.";
        if (shipmentLines.Any(x => !shipments.TryGetValue(x.ShipmentId, out var shipment) || shipment.CompanyId != companyId || shipment.Status != ShipmentStatus.Shipped))
            return "Only shipped documents can be invoiced.";
        if (shipmentLines.Any(x => x.QtyShipped <= 0 || !orderLines.ContainsKey(x.SalesOrderLineId!.Value)))
            return "Every selected shipment line must have a positive shipped quantity and a valid sales order line.";
        var batchIds = shipments.Values.Where(x => x.ShipmentBatchId.HasValue).Select(x => x.ShipmentBatchId!.Value).Distinct().ToList();
        var postedBatchIds = await db.GLBatches.Where(x => batchIds.Contains(x.Id) && x.CompanyId == companyId && x.Status == BatchStatus.Posted)
            .Select(x => x.Id).ToListAsync();
        if (shipments.Values.Any(x => !x.ShipmentBatchId.HasValue || !postedBatchIds.Contains(x.ShipmentBatchId.Value)))
            return "Shipments must be posted to the ledger before they can be invoiced.";
        var priorQuantities = await db.SalesInvoiceLines.Where(x => x.SalesOrderLineId.HasValue && orderLineIds.Contains(x.SalesOrderLineId.Value))
            .GroupBy(x => x.SalesOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.QuantityInvoiced) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
        if (shipmentLines.GroupBy(x => x.SalesOrderLineId!.Value).Any(x => x.Sum(y => y.QtyShipped) + priorQuantities.GetValueOrDefault(x.Key) > orderLines[x.Key].Quantity))
            return "Selected shipments exceed the uninvoiced order quantity.";
        if (shipments.Values.Any(x => x.WarehouseId == Guid.Empty)) return "All source shipments need a warehouse before invoice grouping.";

        foreach (var group in shipmentLines.GroupBy(x =>
        {
            var order = orders[orderLines[x.SalesOrderLineId!.Value].HeaderId];
            return (order.CompanyId, order.CustomerId, order.CurrencyId);
        }))
        {
            var groupOrders = group.Select(x => orders[orderLines[x.SalesOrderLineId!.Value].HeaderId]).DistinctBy(x => x.Id).ToList();
            if (destinationTerms == null && !HasCompatibleSalesOrderTerms(groupOrders)) return "Selected sales orders share customer, site and currency but have incompatible tax, discount or withholding settings.";
            var first = groupOrders[0];
            var (termsError, selectedTerms, selectedTax, selectedWithholding) = await ResolveTermsAsync(db, companyId, destinationTerms, group.Key.CustomerId, Guid.Empty, group.Key.CurrencyId);
            if (termsError != null) return termsError;
            if (selectedWithholding != null && (!selectedWithholding.SalesGlAccountId.HasValue || selectedWithholding.SalesGlAccountId == Guid.Empty || !await db.SegChartOfAccounts.AnyAsync(a => a.Id == selectedWithholding.SalesGlAccountId && a.CompanyId == companyId && a.IsActive)))
                return "The selected withholding tax needs an active sales receivable account.";
            var invoice = new SalesInvoice
            {
                Id = Guid.NewGuid(), CompanyId = companyId, CustomerId = group.Key.CustomerId,
                WarehouseId = group.Select(x => shipments[x.ShipmentId].WarehouseId).Distinct().Count() == 1 ? shipments[group.First().ShipmentId].WarehouseId : null, CurrencyId = group.Key.CurrencyId,
                SalesOrderId = groupOrders.Count == 1 ? groupOrders[0].Id : null,
                InvoiceNumber = await _numbering.GenerateAndReserveNextNumberAsync(companyId, SequenceTargetType.AR_Invoice),
                Date = DateOnly.FromDateTime(selectedTerms?.InvoiceDate?.Date ?? DateTime.Today), TransactionDateTime = DateTime.Now,
                Status = SalesInvoiceStatus.Draft, ProjectId = first.ProjectId,
                ExchangeRate = first.ExchangeRate, TaxId = first.TaxId, TaxGLAccountId = first.TaxGLAccountId,
                DiscountPercentage = first.DiscountPercentage, DiscountAmount = first.DiscountAmount,
                DiscountGlAccountId = first.DiscountGlAccountId,
                WithholdingTaxId = first.WithholdingTaxId, WithholdingTaxName = first.WithholdingTaxName,
                WithholdingPercentage = first.WithholdingPercentage, WithholdingSalesGlAccountId = first.WithholdingSalesGlAccountId,
                ReceivablesGlAccountId = first.ReceivablesGlAccountId,
                DirectIncomeGlAccountId = first.DirectIncomeGlAccountId,
                CustomTransactionTypeId = first.CustomTransactionTypeId
            };
            if (selectedTerms != null)
            {
                invoice.ExchangeRate = selectedTerms.ExchangeRate;
                invoice.TaxId = selectedTax?.Id;
                invoice.TaxGLAccountId = selectedTax?.GLAccountId;
                invoice.DiscountPercentage = selectedTerms.DiscountPercentage;
                invoice.DiscountAmount = selectedTerms.DiscountAmount;
                invoice.WithholdingTaxId = selectedWithholding?.Id;
                invoice.WithholdingTaxName = selectedWithholding?.Name;
                invoice.WithholdingPercentage = selectedWithholding?.PercentageValue ?? 0;
                invoice.WithholdingSalesGlAccountId = selectedWithholding?.SalesGlAccountId;
            }
            foreach (var shipmentLine in group)
            {
                var order = orders[orderLines[shipmentLine.SalesOrderLineId!.Value].HeaderId];
                var orderLine = orderLines[shipmentLine.SalesOrderLineId!.Value];
                invoice.Lines.Add(new SalesInvoiceLine
                { DimensionSetId = shipmentLine.DimensionSetId,
                    Id = Guid.NewGuid(), SalesInvoiceId = invoice.Id, ShipmentLineId = shipmentLine.Id,
                    SalesOrderLineId = orderLine.Id, ItemId = orderLine.ItemId,
                    RevenueGlAccountId = orderLine.Item?.SalesIncomeAccountId,
                    Description = orderLine.Description, UomId = shipmentLine.UomId,
                    UomName = shipmentLine.UomName, UomConversionFactor = shipmentLine.UomConversionFactor,
                    QuantityInvoiced = shipmentLine.QtyShipped, UnitPrice = orderLine.UnitPrice,
                    TaxId = selectedTerms != null ? invoice.TaxId : order.TaxId, TaxGLAccountId = selectedTerms != null ? invoice.TaxGLAccountId : order.TaxGLAccountId
                });
            }
            var gross = invoice.Lines.Sum(x => x.Amount);
            var discount = invoice.DiscountPercentage > 0 ? gross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, gross);
            var taxRate = invoice.TaxId.HasValue
                ? (await db.Taxes.FirstOrDefaultAsync(x => x.Id == invoice.TaxId.Value))?.Per ?? 0m
                : 0m;
            invoice.TotalAmountForeign = gross - discount + (gross - discount) * taxRate / 100m;
            invoice.TotalAmount = Math.Round(invoice.TotalAmountForeign * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            db.SalesInvoices.Add(invoice);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return string.Empty;
    }

    private static async Task<string?> ValidateSourceUomsAsync(AppDbContext db, Guid companyId, List<(Guid ItemId, Guid? UomId, decimal Factor)> lines)
    {
        var itemIds = lines.Select(x => x.ItemId).Distinct().ToList();
        var items = await db.Items.Include(x => x.UomConversions).Where(x => x.CompanyId == companyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var rules = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive).ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
        var uomIds = await db.UnitOfMeasures.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.Id).ToHashSetAsync();
        foreach (var line in lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item)) return "A source item is unavailable in this company.";
            if (line.UomId.HasValue && (!uomIds.Contains(line.UomId.Value) || (line.UomId != item.UomId && !UomConversion.IsConfigured(item, line.UomId)))) return "A selected unit of measure is unavailable for its item.";
            if (line.Factor <= 0 || Math.Abs(line.Factor - UomConversion.FactorFor(item, line.UomId, rules)) > 0.000000000000001m) return "A selected unit of measure has an invalid conversion factor.";
        }
        return null;
    }

    private static async Task<(string? Error, DestinationDocumentTerms? Terms, Tax? Tax, WithholdingTax? Withholding)> ResolveTermsAsync(
        AppDbContext db, Guid companyId, IReadOnlyCollection<DestinationDocumentTerms>? allTerms, Guid partnerId, Guid siteId, Guid currencyId)
    {
        if (allTerms == null) return (null, null, null, null);
        var matches = allTerms.Where(x => x.PartnerId == partnerId && x.SiteId == siteId && x.CurrencyId == currencyId).ToList();
        if (matches.Count != 1) return ("Enter terms for every destination supplier/customer, site and currency group.", null, null, null);
        var terms = matches[0];
        if (!terms.Confirmed) return ("Review and confirm the terms for each destination document.", null, null, null);
        var currencyError = await DocumentCurrencyPolicy.ValidateAsync(db, companyId, currencyId, terms.ExchangeRate);
        if (currencyError != null) return (currencyError, null, null, null);
        if (terms.ExchangeRate <= 0 || terms.DiscountPercentage < 0 || terms.DiscountPercentage > 100 || terms.DiscountAmount < 0 || (terms.DiscountPercentage > 0 && terms.DiscountAmount > 0))
            return ("Enter a positive exchange rate and either a valid discount percentage or a discount amount.", null, null, null);
        var tax = terms.TaxId.HasValue ? await db.Taxes.FirstOrDefaultAsync(x => x.Id == terms.TaxId && x.CompanyId == companyId) : null;
        if (terms.TaxId.HasValue && tax == null) return ("The selected tax is unavailable for this company.", null, null, null);
        var withholding = terms.WithholdingTaxId.HasValue ? await db.WithholdingTaxes.FirstOrDefaultAsync(x => x.Id == terms.WithholdingTaxId && x.CompanyId == companyId) : null;
        if (terms.WithholdingTaxId.HasValue && withholding == null) return ("The selected withholding tax is unavailable for this company.", null, null, null);
        return (null, terms, tax, withholding);
    }

    private static string? ValidateSelection(IReadOnlyCollection<Guid> ids)
    {
        if (ids == null || ids.Count == 0) return "Select at least one document line.";
        if (ids.Any(x => x == Guid.Empty)) return "A selected line has an invalid identifier.";
        if (ids.Distinct().Count() != ids.Count) return "A line cannot be selected more than once.";
        return null;
    }

    private static bool HasTransientSqlError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 1205 || sql.Number == 1222 || sql.Number == -2)) return true;
        return false;
    }

    private static bool HasCompatiblePurchaseTerms(IReadOnlyCollection<PurchaseRequest> docs) => docs.Select(x =>
        (x.TaxId, x.TaxGlAccountId, x.DiscountPercentage, x.DiscountAmount, x.DiscountGlAccountId,
         x.WithholdingTaxId, x.WithholdingPercentage, x.WithholdingGlAccountId, x.ExchangeRate)).Distinct().Count() == 1;

    private static bool HasCompatibleSalesTerms(IReadOnlyCollection<SalesQuote> docs) => docs.Select(x =>
        (x.TaxId, x.TaxGlAccountId, x.DiscountPercentage, x.DiscountAmount, x.DiscountGlAccountId,
         x.WithholdingTaxId, x.WithholdingPercentage, x.WithholdingSalesGlAccountId, x.ExchangeRate)).Distinct().Count() == 1;

    private static bool HasCompatiblePurchaseOrderTerms(IReadOnlyCollection<PurchaseOrder> docs) => docs.Select(x =>
        (x.TaxId, x.TaxGLAccountId, x.DiscountPercentage, x.DiscountAmount, x.DiscountGlAccountId,
         x.WithholdingTaxId, x.WithholdingPercentage, x.WithholdingGlAccountId, x.ExchangeRate)).Distinct().Count() == 1;

    private static bool HasCompatibleSalesOrderTerms(IReadOnlyCollection<SalesOrder> docs) => docs.Select(x =>
        (x.TaxId, x.TaxGLAccountId, x.DiscountPercentage, x.DiscountAmount, x.DiscountGlAccountId,
         x.WithholdingTaxId, x.WithholdingPercentage, x.WithholdingSalesGlAccountId, x.ExchangeRate)).Distinct().Count() == 1;
}
