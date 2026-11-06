using System.Data;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

internal sealed class NewInvoiceReturnService(IDbContextFactory<AppDbContext> factory, GLOperationsService gl, IDocumentNumberingService numbering)
{
    public static async Task<(decimal Paid, decimal Claimed)> SalesCashAsync(AppDbContext db, SalesInvoice invoice, Guid? exceptId = null, Guid? exceptCreditId = null)
    {
        var paid = await (from app in db.PaymentApplications join payment in db.CustomerPayments on app.CustomerPaymentId equals payment.Id
            where app.InvoiceId == invoice.Id && payment.CompanyId == invoice.CompanyId && payment.CustomerId == invoice.CustomerId
                && payment.Status == PaymentStatus.Posted && (!payment.GLBatchId.HasValue || db.GLBatches.Any(b => b.Id == payment.GLBatchId.Value && b.Status == BatchStatus.Posted))
            select app.AppliedAmount).SumAsync();
        var claimed = await db.ReceiptRefunds.Where(x => x.SalesInvoiceId == invoice.Id && x.Id != exceptId && x.Status != ReceiptRefundStatus.Void
            && (x.RefundType == ReceiptRefundType.PaymentOnly || x.RefundType == ReceiptRefundType.Both)
            && (x.Status == ReceiptRefundStatus.Draft || !x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .SumAsync(x => x.TotalAmount);
        claimed += await db.CreditNotes.Where(x => x.SalesInvoiceId == invoice.Id && x.Id != exceptCreditId && !x.ReturnToStock && x.Status != CreditNoteStatus.Void
            && (x.Status == CreditNoteStatus.Draft || (x.Status == CreditNoteStatus.Posted && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))))
            .SumAsync(x => x.TotalAmount);
        return (paid, claimed);
    }

    public static async Task<(decimal Paid, decimal Claimed)> PurchaseCashAsync(AppDbContext db, PurchaseInvoice invoice, Guid? exceptId = null)
    {
        var paidBase = await db.Set<VendorPayment>().Where(x => x.PurchaseInvoiceId == invoice.Id && x.Amount > 0
            && (!x.GLBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GLBatchId.Value && b.Status == BatchStatus.Posted))).SumAsync(x => x.Amount);
        var claimed = await db.VendorReturns.Where(x => x.PurchaseInvoiceId == invoice.Id && x.Id != exceptId && x.Status != VendorReturnStatus.Void
            && (x.ReturnType == VendorReturnType.PaymentOnly || x.ReturnType == VendorReturnType.Both)
            && (x.Status == VendorReturnStatus.Draft || !x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId.Value && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted))))
            .SumAsync(x => x.TotalAmount);
        return (Math.Round(paidBase / (invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1), 4), claimed);
    }

    public async Task<ReceiptRefund> CreateSalesAsync(Guid invoiceId, ReceiptRefundType type, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.SalesInvoices.Include(x => x.Lines).ThenInclude(x => x.Item).FirstOrDefaultAsync(x => x.Id == invoiceId)
            ?? throw new InvalidOperationException("Sales invoice not found.");
        if (!await NewInvoiceCorrectionService.IsPostedSalesAsync(db, invoice)) throw new InvalidOperationException("Only a posted invoice can be returned or refunded.");
        var refund = new ReceiptRefund { CompanyId = invoice.CompanyId, SalesInvoiceId = invoice.Id, CustomerId = invoice.CustomerId,
            CurrencyId = invoice.CurrencyId, ExchangeRate = invoice.ExchangeRate, ProjectId = invoice.ProjectId, RefundType = type,
            RefundNumber = await numbering.GenerateAndReserveNextNumberAsync(invoice.CompanyId, SequenceTargetType.AR_ReceiptRefund), CreatedByUserId = userId };
        if (type != ReceiptRefundType.PaymentOnly)
            foreach (var line in invoice.Lines.Where(x => x.Item != null && !x.Item.IsService))
            {
                var b = await InvoiceCorrectionBalances.SalesAsync(db, line);
                var available = Math.Max(0, b.Fulfilled - b.PriorReturned - b.OtherRootReturned);
                if (available > 0) refund.Lines.Add(new ReceiptRefundLine { DimensionSetId = line.DimensionSetId,  SalesInvoiceLineId = line.Id, SalesOrderLineId = line.SalesOrderLineId,
                    ItemId = line.ItemId!.Value, UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor,
                    UnitPrice = line.UnitPrice, OriginalShippedQty = b.Fulfilled, PreviouslyReturnedQty = b.PriorReturned + b.OtherRootReturned, MaxAdjustableQty = available });
            }
        if (type == ReceiptRefundType.QuantityOnly && refund.Lines.Count == 0) throw new InvalidOperationException("This invoice has no shipped quantities available to return.");
        if (type != ReceiptRefundType.QuantityOnly) { var cash = await SalesCashAsync(db, invoice); if (cash.Paid <= cash.Claimed) throw new InvalidOperationException("This invoice has no paid cash available for refund."); }
        db.ReceiptRefunds.Add(refund); await db.SaveChangesAsync(); return refund;
    }

    public async Task<VendorReturn> CreatePurchaseAsync(Guid invoiceId, VendorReturnType type, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.PurchaseInvoices.Include(x => x.Lines).ThenInclude(x => x.Item).FirstOrDefaultAsync(x => x.Id == invoiceId)
            ?? throw new InvalidOperationException("Purchase invoice not found.");
        if (!await NewInvoiceCorrectionService.IsPostedPurchaseAsync(db, invoice)) throw new InvalidOperationException("Only a posted invoice can be returned or refunded.");
        var result = new VendorReturn { CompanyId = invoice.CompanyId, PurchaseInvoiceId = invoice.Id, VendorId = invoice.VendorId,
            CurrencyId = invoice.CurrencyId, ExchangeRate = invoice.ExchangeRate, ProjectId = invoice.ProjectId, ReturnType = type,
            ReturnNumber = await numbering.GenerateAndReserveNextNumberAsync(invoice.CompanyId, SequenceTargetType.AP_ReturnToVendor), CreatedByUserId = userId };
        if (type != VendorReturnType.PaymentOnly)
            foreach (var line in invoice.Lines.Where(x => x.Item != null && !x.Item.IsService))
            {
                var b = await InvoiceCorrectionBalances.PurchaseAsync(db, line, approvedFulfillmentOnly: true);
                var available = Math.Max(0, b.Fulfilled - b.PriorReturned - b.OtherRootReturned);
                if (available > 0) result.Lines.Add(new VendorReturnLine { DimensionSetId = line.DimensionSetId,  PurchaseInvoiceLineId = line.Id, PurchaseOrderLineId = line.PurchaseOrderLineId,
                    ItemId = line.ItemId!.Value, UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor,
                    UnitCost = line.UnitCostBilled, OriginalReceivedQty = b.Fulfilled, PreviouslyReturnedQty = b.PriorReturned + b.OtherRootReturned, MaxReturnableQty = available });
            }
        if (type == VendorReturnType.QuantityOnly && result.Lines.Count == 0) throw new InvalidOperationException("This invoice has no received quantities available to return.");
        if (type != VendorReturnType.QuantityOnly) { var cash = await PurchaseCashAsync(db, invoice); if (cash.Paid <= cash.Claimed) throw new InvalidOperationException("This invoice has no paid cash available for refund."); }
        db.VendorReturns.Add(result); await db.SaveChangesAsync(); return result;
    }

    public static async Task EnrichSalesAsync(AppDbContext db, ReceiptRefund refund)
    {
        var sources = await db.SalesInvoiceLines.Where(x => x.SalesInvoiceId == refund.SalesInvoiceId).ToDictionaryAsync(x => x.Id);
        foreach (var line in refund.Lines)
            if (line.SalesInvoiceLineId.HasValue && sources.TryGetValue(line.SalesInvoiceLineId.Value, out var source))
            { var b = await InvoiceCorrectionBalances.SalesAsync(db, source, exceptReturn: refund.Id); line.OriginalShippedQty = b.Fulfilled; line.PreviouslyReturnedQty = b.PriorReturned + b.OtherRootReturned; line.MaxAdjustableQty = Math.Max(0, b.Fulfilled - b.PriorReturned - b.OtherRootReturned); }
    }

    public static async Task EnrichPurchaseAsync(AppDbContext db, VendorReturn result)
    {
        var sources = await db.PurchaseInvoiceLines.Where(x => x.PurchaseInvoiceId == result.PurchaseInvoiceId).ToDictionaryAsync(x => x.Id);
        foreach (var line in result.Lines)
            if (line.PurchaseInvoiceLineId.HasValue && sources.TryGetValue(line.PurchaseInvoiceLineId.Value, out var source))
            { var b = await InvoiceCorrectionBalances.PurchaseAsync(db, source, exceptReturn: result.Id, approvedFulfillmentOnly: true); line.OriginalReceivedQty = b.Fulfilled; line.PreviouslyReturnedQty = b.PriorReturned + b.OtherRootReturned; line.MaxReturnableQty = Math.Max(0, b.Fulfilled - b.PriorReturned - b.OtherRootReturned); }
    }

    public Task<string> SaveSalesAsync(ReceiptRefund input) => ProcessSalesAsync(input.Id, input, null);
    public Task<string> PostSalesAsync(Guid id, Guid userId) => ProcessSalesAsync(id, null, userId);
    public Task<string> SavePurchaseAsync(VendorReturn input) => ProcessPurchaseAsync(input.Id, input, null);
    public Task<string> PostPurchaseAsync(Guid id, Guid userId) => ProcessPurchaseAsync(id, null, userId);

    private static async Task<bool> ValidUnitAsync(AppDbContext db, Guid companyId, Item? item, Guid? uomId, decimal factor)
    {
        if (item == null) return false;
        var factors = await db.ItemUomConversionLines.AsNoTracking().Where(x => x.ItemId == item.Id && x.IsActive)
            .GroupBy(x => x.UomId).ToDictionaryAsync(x => x.Key, x => x.First().ConversionFactorToBase);
        var conversions = await db.UomConversionRules.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive)
            .ToDictionaryAsync(x => (x.FromUomId, x.ToUomId), x => x.ConversionFactor);
        if (uomId.HasValue && uomId != item.UomId && !factors.ContainsKey(uomId.Value)
            && !UomConversion.IsConfigured(item, uomId)) return false;
        return Math.Abs(UomConversion.NormalizeFactor(factor) - UomConversion.FactorFor(item, uomId, factors, conversions)) <= 0.000000000000001m;
    }

    private async Task<string> ProcessPurchaseAsync(Guid id, VendorReturn? input, Guid? userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var doc = await db.VendorReturns.FromSqlInterpolated($"SELECT * FROM [VendorReturns] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").Include(x => x.Lines).ThenInclude(x => x.Item).FirstOrDefaultAsync();
            if (doc?.PurchaseInvoiceId == null) return "Invoice return not found.";
            var batch = doc.GlBatchId.HasValue ? await db.GLBatches.FindAsync(doc.GlBatchId.Value) : null;
            if (doc.Status == VendorReturnStatus.Void || (doc.Status == VendorReturnStatus.Posted && (batch == null || batch.Status == BatchStatus.Posted || batch.Status == BatchStatus.Ready))) return "This return is already submitted or locked.";
            var invoice = await db.PurchaseInvoices.FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {doc.PurchaseInvoiceId.Value}").Include(x => x.Lines).Include(x => x.Vendor).FirstOrDefaultAsync();
            if (invoice == null || invoice.CompanyId != doc.CompanyId || invoice.VendorId != doc.VendorId || invoice.CurrencyId != doc.CurrencyId || !await NewInvoiceCorrectionService.IsPostedPurchaseAsync(db, invoice)) return "The source invoice is unavailable or not posted.";
            var lineIds = invoice.Lines.Select(x => x.Id).ToList();
            await db.PurchaseInvoiceLines.FromSqlRaw("SELECT * FROM [PurchaseInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => lineIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var roots = invoice.Lines.Where(x => x.PurchaseOrderLineId.HasValue).Select(x => x.PurchaseOrderLineId!.Value).Distinct().OrderBy(x => x).ToList();
            await db.PurchaseOrderLines.FromSqlRaw("SELECT * FROM [PurchaseOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => roots.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            if (input != null)
            {
                if (!Enum.IsDefined(input.ReturnType) || input.Lines.Count != doc.Lines.Count || input.Lines.Select(x => x.Id).Distinct().Count() != doc.Lines.Count) return "The return lines are invalid; reload the document.";
                foreach (var line in doc.Lines)
                {
                    var supplied = input.Lines.SingleOrDefault(x => x.Id == line.Id);
                    if (supplied == null || supplied.PurchaseInvoiceLineId != line.PurchaseInvoiceLineId || supplied.ItemId != line.ItemId || supplied.PurchaseOrderLineId != line.PurchaseOrderLineId || !await ValidUnitAsync(db, doc.CompanyId, line.Item, supplied.UomId, supplied.UomConversionFactor)) return "A return line or its unit is invalid. Reload the document.";
                    line.DimensionSetId = supplied.DimensionSetId; line.Quantity = supplied.Quantity; line.UomId = supplied.UomId; line.UomName = supplied.UomName; line.UomConversionFactor = supplied.UomConversionFactor;
                }
                doc.DimensionSetId = input.DimensionSetId; doc.Date = input.Date; doc.TransactionDateTime = input.TransactionDateTime; doc.Reason = input.Reason; doc.ReturnType = input.ReturnType;
                doc.TotalAmount = input.TotalAmount; doc.BankAccountId = input.BankAccountId; doc.WarehouseId = input.WarehouseId; doc.CustomTransactionTypeId = input.CustomTransactionTypeId;
                doc.OverrideGrIrClearingGlAccountId = input.OverrideGrIrClearingGlAccountId; doc.OverrideInventoryAssetGlAccountId = input.OverrideInventoryAssetGlAccountId; doc.OverrideAccountsPayableGlAccountId = input.OverrideAccountsPayableGlAccountId;
                if (batch?.Status == BatchStatus.Rejected) { doc.Status = VendorReturnStatus.Draft; doc.GlBatchId = null; }
            }
            if (doc.Lines.Any(x => x.Quantity < 0) || doc.TotalAmount < 0) return "Amounts and quantities cannot be negative.";
            if (doc.ReturnType == VendorReturnType.PaymentOnly && doc.Lines.Any(x => x.Quantity > 0)) return "A payment-only refund cannot return stock.";
            if (doc.ReturnType == VendorReturnType.QuantityOnly) doc.TotalAmount = 0;
            var selected = doc.Lines.Where(x => x.Quantity > 0).ToList();
            if (selected.Any(x => !x.PurchaseInvoiceLineId.HasValue) || selected.Select(x => x.PurchaseInvoiceLineId).Distinct().Count() != selected.Count) return "Each return must reference a different invoice line.";
            var sources = invoice.Lines.ToDictionary(x => x.Id);
            foreach (var line in selected)
            {
                if (!sources.TryGetValue(line.PurchaseInvoiceLineId!.Value, out var source) || source.ItemId != line.ItemId || source.PurchaseOrderLineId != line.PurchaseOrderLineId) return "A return line no longer matches its invoice.";
                var balance = await InvoiceCorrectionBalances.PurchaseAsync(db, source, exceptReturn: doc.Id, approvedFulfillmentOnly: true);
                if (line.Quantity > Math.Max(0, balance.Fulfilled - balance.PriorReturned - balance.OtherRootReturned)) return $"The returned quantity for '{line.Item?.Name}' exceeds its remaining received quantity.";
                if (!await ValidUnitAsync(db, doc.CompanyId, line.Item, line.UomId, line.UomConversionFactor)) return $"The unit for '{line.Item?.Name}' is no longer configured.";
            }
            foreach (var group in selected.Where(x => x.PurchaseOrderLineId.HasValue).GroupBy(x => x.PurchaseOrderLineId!.Value))
            {
                var received = await db.GoodsReceipts.Where(x => !x.GLBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GLBatchId && b.Status == BatchStatus.Posted)).SelectMany(x => x.Lines).Where(x => x.PurchaseOrderLineId == group.Key).SumAsync(x => x.QuantityReceived);
                var returned = await db.VendorReturnLines.Where(x => x.HeaderId != doc.Id && x.PurchaseOrderLineId == group.Key && x.Header!.Status != VendorReturnStatus.Void && x.Header.ReturnType != VendorReturnType.PaymentOnly
                    && (x.Header.Status == VendorReturnStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted)))).SumAsync(x => x.Quantity);
                if (group.Sum(x => x.Quantity) > Math.Max(0, received - returned)) return "The combined return exceeds the remaining received order quantity.";
            }
            if (doc.ReturnType != VendorReturnType.QuantityOnly)
            {
                var cash = await PurchaseCashAsync(db, invoice, doc.Id);
                if (doc.TotalAmount > Math.Max(0, cash.Paid - cash.Claimed)) return "The refund exceeds the remaining cash paid on this invoice.";
            }
            if (!userId.HasValue) { await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty; }
            if (doc.ReturnType != VendorReturnType.PaymentOnly && selected.Count == 0) return "Enter a quantity to return.";
            var entries = new List<GLJournalLine>();
            var ledger = await db.GLTransactions.Where(x => x.BatchId == invoice.GLBatchId && x.CompanyId == invoice.CompanyId).ToListAsync();
            var custom = doc.CustomTransactionTypeId.HasValue ? await db.TransactionGlMappings.FirstOrDefaultAsync(x => x.CompanyId == doc.CompanyId && x.CustomTransactionTypeId == doc.CustomTransactionTypeId) : null;
            var mapping = new TransactionMappingService(factory, gl);
            if (selected.Count > 0)
            {
                if (!doc.WarehouseId.HasValue || !await db.Warehouses.AnyAsync(x => x.Id == doc.WarehouseId && x.CompanyId == doc.CompanyId && (x.Type == WarehouseType.Purchase || x.Type == WarehouseType.Both))) return "Select an available purchase warehouse.";
                foreach (var group in selected.GroupBy(x => x.ItemId))
                {
                    var available = await db.StockLedgers.Where(x => x.CompanyId == doc.CompanyId && x.WarehouseId == doc.WarehouseId && x.ItemId == group.Key).SumAsync(x => x.QuantityChanged);
                    available -= await db.SalesShipmentLines.Where(x => x.ItemId == group.Key && x.Shipment!.CompanyId == doc.CompanyId && x.Shipment.WarehouseId == doc.WarehouseId && x.Shipment.Status == ShipmentStatus.Pending).SumAsync(x => x.QtyShipped);
                    if (group.Sum(x => x.Quantity) > available) return $"Insufficient warehouse stock for '{group.First().Item?.Name}'.";
                }
                foreach (var line in selected)
                {
                    if (line.Item == null || line.Item.IsService) return "Only inventory items can be returned to stock.";
                    var source = sources[line.PurchaseInvoiceLineId!.Value];
                    var gross = Math.Round(source.LineTotal * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                    var reference = !string.IsNullOrWhiteSpace(source.Description) ? source.Description : $"Purchase Invoice: {invoice.ExternalInvoiceNumber}";
                    var accounts = ledger.Where(x => x.Debit == gross && x.Narration == reference).Select(x => x.SegCoaId).Distinct().ToList();
                    var clearing = doc.OverrideGrIrClearingGlAccountId ?? custom?.OverrideDebitGlAccountId ?? (accounts.Count == 1 ? accounts[0] : Guid.Empty);
                    var asset = doc.OverrideInventoryAssetGlAccountId ?? custom?.OverrideCreditGlAccountId ?? await mapping.GetMappedAccountAsync(doc.CompanyId, SystemTransactionType.VendorReturnStock, false, line.Item.InventoryAssetAccountId);
                    var value = Math.Round(line.Quantity * source.UnitCostBilled * invoice.ExchangeRate, 2);
                    if (value > 0 && (clearing == Guid.Empty || asset == Guid.Empty)) return $"The invoice clearing or inventory account for '{line.Item.Name}' could not be verified.";
                    if (value > 0) { entries.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = clearing, Debit = value, Reference = $"Return clearing: {line.Item.Code}" }); entries.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = asset, Credit = value, Reference = $"Returned stock: {line.Item.Code}" }); }
                    db.StockLedgers.Add(new StockLedger { DimensionSetId = line.DimensionSetId,  CompanyId = doc.CompanyId, ItemId = line.ItemId, WarehouseId = doc.WarehouseId.Value, QuantityChanged = -line.Quantity, UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor, QuantityInUom = UomConversion.FromBase(line.Quantity, line.UomConversionFactor), Type = StockMovementType.PurchaseReturn, ProjectId = invoice.ProjectId, CostAtTime = source.UnitCostBilled, Reference = doc.ReturnNumber, Date = DateTime.UtcNow });
                }
            }
            if (doc.ReturnType != VendorReturnType.QuantityOnly)
            {
                if (doc.TotalAmount <= 0 || !doc.BankAccountId.HasValue) return "Enter a positive cash refund and select a bank account.";
                var accounts = ledger.Where(x => x.Credit > 0 && x.Narration == $"Purchase Invoice {invoice.PurchaseInvoiceNumber} - {invoice.Vendor?.Name ?? "Unknown"}").Select(x => x.SegCoaId).Distinct().ToList();
                var ap = doc.OverrideAccountsPayableGlAccountId ?? custom?.OverrideCreditGlAccountId ?? (accounts.Count == 1 ? accounts[0] : Guid.Empty);
                if (ap == Guid.Empty) return "The original payables account could not be verified.";
                var value = Math.Round(doc.TotalAmount * invoice.ExchangeRate, 2);
                if (value <= 0) return "The refund amount is too small to post.";
                entries.Add(new GLJournalLine { SegCoaId = custom?.OverrideDebitGlAccountId ?? doc.BankAccountId.Value, Debit = value, Reference = $"Refund: {doc.ReturnNumber}" });
                entries.Add(new GLJournalLine { SegCoaId = ap, Credit = value, Reference = $"Refund: {invoice.PurchaseInvoiceNumber}" });
            }
            if (entries.Count == 0) return "The return has no accounting value to submit.";
            IEnumerable<(Guid? SetId, decimal Weight)> refundWeights = doc.DimensionSetId.HasValue
                ? new[] { (doc.DimensionSetId, 1m) }
                : selected.Count > 0 ? selected.Select(x => (x.DimensionSetId, Math.Abs(x.Quantity * x.UnitCost)))
                : invoice.Lines.Select(x => (x.DimensionSetId, Math.Abs(x.LineTotal)));
            if (doc.ReturnType == VendorReturnType.PaymentOnly && !doc.DimensionSetId.HasValue)
                refundWeights = await DimensionService.PostedInvoiceWeightsAsync(db, doc.CompanyId, invoice.GLBatchId, false, "Purchase Invoice " + invoice.PurchaseInvoiceNumber + " - ", refundWeights);
            DimensionService.AllocateSummaryLines(entries, refundWeights);
            var (error, batchId) = await gl.StageNewSubledgerBatchAsync(db, doc.CompanyId, doc.Date, "Vendor Return", doc.ReturnNumber, doc.ReturnNumber, entries, userId.Value.ToString(), invoice.ProjectId);
            if (!string.IsNullOrEmpty(error) || !batchId.HasValue) return $"Unable to submit return: {error}";
            foreach (var movement in db.ChangeTracker.Entries<StockLedger>().Where(x => x.State == EntityState.Added)) movement.Entity.GLBatchId = batchId;
            doc.Status = VendorReturnStatus.Posted; doc.GlBatchId = batchId; doc.PostedAt = DateTime.UtcNow; doc.PostedByUserId = userId;
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (DbUpdateException) { return "The return could not be saved. Reload and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice or source quantity is busy. Reload and try again."; }
    }

    private async Task<string> ProcessSalesAsync(Guid id, ReceiptRefund? input, Guid? userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var doc = await db.ReceiptRefunds.FromSqlInterpolated($"SELECT * FROM [ReceiptRefunds] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").Include(x => x.Lines).ThenInclude(x => x.Item).FirstOrDefaultAsync();
            if (doc?.SalesInvoiceId == null) return "Invoice return not found.";
            var batch = doc.GlBatchId.HasValue ? await db.GLBatches.FindAsync(doc.GlBatchId.Value) : null;
            if (doc.Status == ReceiptRefundStatus.Void || (doc.Status == ReceiptRefundStatus.Posted && (batch == null || batch.Status == BatchStatus.Posted || batch.Status == BatchStatus.Ready))) return "This return is already submitted or locked.";
            var invoice = await db.SalesInvoices.FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {doc.SalesInvoiceId.Value}").Include(x => x.Lines).FirstOrDefaultAsync();
            if (invoice == null || invoice.CompanyId != doc.CompanyId || invoice.CustomerId != doc.CustomerId || invoice.CurrencyId != doc.CurrencyId || !await NewInvoiceCorrectionService.IsPostedSalesAsync(db, invoice)) return "The source invoice is unavailable or not posted.";
            var lineIds = invoice.Lines.Select(x => x.Id).ToList();
            await db.SalesInvoiceLines.FromSqlRaw("SELECT * FROM [SalesInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => lineIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var roots = invoice.Lines.Where(x => x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).Distinct().OrderBy(x => x).ToList();
            await db.SalesOrderLines.FromSqlRaw("SELECT * FROM [SalesOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => roots.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            if (input != null)
            {
                if (!Enum.IsDefined(input.RefundType) || input.Lines.Count != doc.Lines.Count || input.Lines.Select(x => x.Id).Distinct().Count() != doc.Lines.Count) return "The return lines are invalid; reload the document.";
                foreach (var line in doc.Lines)
                {
                    var supplied = input.Lines.SingleOrDefault(x => x.Id == line.Id);
                    if (supplied == null || supplied.SalesInvoiceLineId != line.SalesInvoiceLineId || supplied.ItemId != line.ItemId || supplied.SalesOrderLineId != line.SalesOrderLineId || !await ValidUnitAsync(db, doc.CompanyId, line.Item, supplied.UomId, supplied.UomConversionFactor)) return "A return line or its unit is invalid. Reload the document.";
                    line.DimensionSetId = supplied.DimensionSetId; line.Quantity = supplied.Quantity; line.UomId = supplied.UomId; line.UomName = supplied.UomName; line.UomConversionFactor = supplied.UomConversionFactor;
                }
                doc.DimensionSetId = input.DimensionSetId; doc.Date = input.Date; doc.TransactionDateTime = input.TransactionDateTime; doc.Reason = input.Reason; doc.RefundType = input.RefundType;
                doc.TotalAmount = input.TotalAmount; doc.BankAccountId = input.BankAccountId; doc.DestinationWarehouseId = input.DestinationWarehouseId; doc.CustomTransactionTypeId = input.CustomTransactionTypeId;
                doc.OverrideReceivablesGlAccountId = input.OverrideReceivablesGlAccountId; doc.OverrideInventoryAssetGlAccountId = input.OverrideInventoryAssetGlAccountId; doc.OverrideCogsGlAccountId = input.OverrideCogsGlAccountId;
                if (batch?.Status == BatchStatus.Rejected) { doc.Status = ReceiptRefundStatus.Draft; doc.GlBatchId = null; }
            }
            if (doc.Lines.Any(x => x.Quantity < 0) || doc.TotalAmount < 0) return "Amounts and quantities cannot be negative.";
            if (doc.RefundType == ReceiptRefundType.PaymentOnly && doc.Lines.Any(x => x.Quantity > 0)) return "A payment-only refund cannot return stock.";
            if (doc.RefundType == ReceiptRefundType.QuantityOnly) doc.TotalAmount = 0;
            var selected = doc.Lines.Where(x => x.Quantity > 0).ToList();
            if (selected.Any(x => !x.SalesInvoiceLineId.HasValue) || selected.Select(x => x.SalesInvoiceLineId).Distinct().Count() != selected.Count) return "Each return must reference a different invoice line.";
            var sources = invoice.Lines.ToDictionary(x => x.Id);
            foreach (var line in selected)
            {
                if (!sources.TryGetValue(line.SalesInvoiceLineId!.Value, out var source) || source.ItemId != line.ItemId || source.SalesOrderLineId != line.SalesOrderLineId) return "A return line no longer matches its invoice.";
                var balance = await InvoiceCorrectionBalances.SalesAsync(db, source, exceptReturn: doc.Id);
                if (line.Quantity > Math.Max(0, balance.Fulfilled - balance.PriorReturned - balance.OtherRootReturned)) return $"The returned quantity for '{line.Item?.Name}' exceeds its remaining shipped quantity.";
                if (!await ValidUnitAsync(db, doc.CompanyId, line.Item, line.UomId, line.UomConversionFactor)) return $"The unit for '{line.Item?.Name}' is no longer configured.";
            }
            foreach (var group in selected.Where(x => x.SalesOrderLineId.HasValue).GroupBy(x => x.SalesOrderLineId!.Value))
            {
                var stored = await db.SalesOrderLines.Where(x => x.Id == group.Key).Select(x => x.QtyShipped).SingleAsync();
                var shipped = await db.SalesShipmentLines.Where(x => x.SalesOrderLineId == group.Key && x.Shipment!.Status == ShipmentStatus.Shipped).SumAsync(x => x.QtyShipped);
                var returned = await db.ReceiptRefundLines.Where(x => x.HeaderId != doc.Id && x.SalesOrderLineId == group.Key && x.Header!.Status != ReceiptRefundStatus.Void && x.Header.RefundType != ReceiptRefundType.PaymentOnly
                    && (x.Header.Status == ReceiptRefundStatus.Draft || !x.Header.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.Header.GlBatchId && (b.Status == BatchStatus.Ready || b.Status == BatchStatus.Posted)))).SumAsync(x => x.Quantity);
                if (group.Sum(x => x.Quantity) > Math.Max(0, Math.Max(stored, shipped) - returned)) return "The combined return exceeds the remaining shipped order quantity.";
            }
            if (doc.RefundType != ReceiptRefundType.QuantityOnly)
            {
                var cash = await SalesCashAsync(db, invoice, doc.Id);
                if (doc.TotalAmount > Math.Max(0, cash.Paid - cash.Claimed)) return "The refund exceeds the remaining cash paid on this invoice.";
            }
            if (!userId.HasValue) { await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty; }
            if (doc.RefundType != ReceiptRefundType.PaymentOnly && selected.Count == 0) return "Enter a quantity to return.";
            var entries = new List<GLJournalLine>();
            var custom = doc.CustomTransactionTypeId.HasValue ? await db.TransactionGlMappings.FirstOrDefaultAsync(x => x.CompanyId == doc.CompanyId && x.CustomTransactionTypeId == doc.CustomTransactionTypeId) : null;
            var mapping = new TransactionMappingService(factory, gl);
            if (selected.Count > 0)
            {
                if (!doc.DestinationWarehouseId.HasValue || !await db.Warehouses.AnyAsync(x => x.Id == doc.DestinationWarehouseId && x.CompanyId == doc.CompanyId && (x.Type == WarehouseType.Sales || x.Type == WarehouseType.Both))) return "Select an available sales warehouse.";
                foreach (var line in selected)
                {
                    if (line.Item == null || line.Item.IsService) return "Only inventory items can be returned to stock.";
                    var cost = line.Item.CurrentDisplayCost;
                    var value = Math.Round(line.Quantity * cost, 2);
                    var asset = doc.OverrideInventoryAssetGlAccountId ?? custom?.OverrideDebitGlAccountId ?? await mapping.GetMappedAccountAsync(doc.CompanyId, SystemTransactionType.ReceiptRefundStock, true, line.Item.InventoryAssetAccountId);
                    var cogs = doc.OverrideCogsGlAccountId ?? custom?.OverrideCreditGlAccountId ?? await mapping.GetMappedAccountAsync(doc.CompanyId, SystemTransactionType.ReceiptRefundStock, false, line.Item.CostOfGoodsSoldAccountId);
                    if (value > 0 && (asset == Guid.Empty || cogs == Guid.Empty)) return $"Inventory and cost accounts are required for '{line.Item.Name}'.";
                    if (value > 0) { entries.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = asset, Debit = value, Reference = $"Returned stock: {line.Item.Code}" }); entries.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = cogs, Credit = value, Reference = $"Returned cost: {line.Item.Code}" }); }
                    db.StockLedgers.Add(new StockLedger { DimensionSetId = line.DimensionSetId,  CompanyId = doc.CompanyId, ItemId = line.ItemId, WarehouseId = doc.DestinationWarehouseId.Value, QuantityChanged = line.Quantity, UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor, QuantityInUom = UomConversion.FromBase(line.Quantity, line.UomConversionFactor), Type = StockMovementType.SalesReturn, ProjectId = invoice.ProjectId, CostAtTime = cost, Reference = doc.RefundNumber, Date = DateTime.UtcNow });
                }
            }
            if (doc.RefundType != ReceiptRefundType.QuantityOnly)
            {
                if (doc.TotalAmount <= 0 || !doc.BankAccountId.HasValue) return "Enter a positive cash refund and select a bank account.";
                var accounts = await db.GLTransactions.Where(x => x.BatchId == invoice.GLBatchId && x.CompanyId == invoice.CompanyId && x.Debit > 0 && x.Narration == $"AR {invoice.InvoiceNumber}").Select(x => x.SegCoaId).Distinct().ToListAsync();
                var ar = doc.OverrideReceivablesGlAccountId ?? custom?.OverrideDebitGlAccountId ?? (accounts.Count == 1 ? accounts[0] : Guid.Empty);
                if (ar == Guid.Empty) return "The original receivables account could not be verified.";
                var value = Math.Round(doc.TotalAmount * invoice.ExchangeRate, 2);
                if (value <= 0) return "The refund amount is too small to post.";
                entries.Add(new GLJournalLine { SegCoaId = ar, Debit = value, Reference = $"Refund: {invoice.InvoiceNumber}" });
                entries.Add(new GLJournalLine { SegCoaId = custom?.OverrideCreditGlAccountId ?? doc.BankAccountId.Value, Credit = value, Reference = $"Refund: {doc.RefundNumber}" });
            }
            if (entries.Count == 0) return "The return has no accounting value to submit.";
            IEnumerable<(Guid? SetId, decimal Weight)> refundWeights = doc.DimensionSetId.HasValue
                ? new[] { (doc.DimensionSetId, 1m) }
                : selected.Count > 0 ? selected.Select(x => (x.DimensionSetId, Math.Abs(x.Quantity * x.UnitPrice)))
                : invoice.Lines.Select(x => (x.DimensionSetId, Math.Abs(x.Amount)));
            if (doc.RefundType == ReceiptRefundType.PaymentOnly && !doc.DimensionSetId.HasValue)
                refundWeights = await DimensionService.PostedInvoiceWeightsAsync(db, doc.CompanyId, invoice.GLBatchId, true, "AR " + invoice.InvoiceNumber, refundWeights);
            DimensionService.AllocateSummaryLines(entries, refundWeights);
            var (error, batchId) = await gl.StageNewSubledgerBatchAsync(db, doc.CompanyId, doc.Date, "Receipt Return Refund", doc.RefundNumber, doc.RefundNumber, entries, userId.Value.ToString(), invoice.ProjectId);
            if (!string.IsNullOrEmpty(error) || !batchId.HasValue) return $"Unable to submit return: {error}";
            foreach (var movement in db.ChangeTracker.Entries<StockLedger>().Where(x => x.State == EntityState.Added)) movement.Entity.GLBatchId = batchId;
            doc.Status = ReceiptRefundStatus.Posted; doc.GlBatchId = batchId; doc.PostedAt = DateTime.UtcNow; doc.PostedByUserId = userId;
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (DbUpdateException) { return "The return could not be saved. Reload and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice or source quantity is busy. Reload and try again."; }
    }
}
