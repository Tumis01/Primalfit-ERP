using System.Data;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

// New invoices have independent line identities. These paths deliberately do
// not manufacture a legacy order to stand in for an invoice.
internal sealed class NewInvoiceCorrectionService(IDbContextFactory<AppDbContext> factory, GLOperationsService gl, IDocumentNumberingService numbering)
{
    public async Task<CreditNote> CreateCreditAsync(Guid invoiceId, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.SalesInvoices.Include(x => x.Lines).ThenInclude(x => x.Item).FirstOrDefaultAsync(x => x.Id == invoiceId)
            ?? throw new InvalidOperationException("Sales invoice not found.");
        if (!await IsPostedSalesAsync(db, invoice)) throw new InvalidOperationException("Only a posted invoice can be adjusted.");
        var note = new CreditNote { CompanyId = invoice.CompanyId, SalesInvoiceId = invoice.Id, CustomerId = invoice.CustomerId,
            CurrencyId = invoice.CurrencyId, ExchangeRate = invoice.ExchangeRate, ProjectId = invoice.ProjectId,
            CreditNoteNumber = await numbering.GenerateAndReserveNextNumberAsync(invoice.CompanyId, SequenceTargetType.AR_CreditNote),
            CreatedByUserId = userId, Reason = "Invoice line adjustment", ReturnToStock = true };
        foreach (var line in invoice.Lines.Where(x => x.QuantityInvoiced > 0))
        {
            var b = await InvoiceCorrectionBalances.SalesAsync(db, line);
            if (b.Invoiced <= b.PriorCorrected) continue;
            note.Lines.Add(new CreditNoteLine { DimensionSetId = line.DimensionSetId,  SalesInvoiceLineId = line.Id, SalesOrderLineId = line.SalesOrderLineId,
                ItemId = line.ItemId, UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor,
                UnitPrice = line.UnitPrice, OriginalSoldQty = b.Invoiced, MaxReturnableQty = b.Invoiced - b.PriorCorrected });
        }
        if (note.Lines.Count == 0) throw new InvalidOperationException("This invoice has no remaining adjustable lines.");
        db.CreditNotes.Add(note); await db.SaveChangesAsync(); return note;
    }

    public async Task<DebitNote> CreateDebitAsync(Guid invoiceId, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var invoice = await db.PurchaseInvoices.Include(x => x.Lines).ThenInclude(x => x.Item).FirstOrDefaultAsync(x => x.Id == invoiceId)
            ?? throw new InvalidOperationException("Purchase invoice not found.");
        if (!await IsPostedPurchaseAsync(db, invoice)) throw new InvalidOperationException("Only a posted invoice can be adjusted.");
        var note = new DebitNote { CompanyId = invoice.CompanyId, PurchaseInvoiceId = invoice.Id, VendorId = invoice.VendorId,
            CurrencyId = invoice.CurrencyId, ExchangeRate = invoice.ExchangeRate, ProjectId = invoice.ProjectId,
            DebitNoteNumber = await numbering.GenerateAndReserveNextNumberAsync(invoice.CompanyId, SequenceTargetType.AP_DebitNote),
            CreatedByUserId = userId, Reason = "Invoice line adjustment", ReturnToStock = true };
        foreach (var line in invoice.Lines.Where(x => x.QuantityBilled > 0))
        {
            var b = await InvoiceCorrectionBalances.PurchaseAsync(db, line);
            if (b.Invoiced <= b.PriorCorrected) continue;
            note.Lines.Add(new DebitNoteLine { DimensionSetId = line.DimensionSetId,  PurchaseInvoiceLineId = line.Id, PurchaseOrderLineId = line.PurchaseOrderLineId,
                ItemId = line.ItemId, UomId = line.UomId, UomName = line.UomName, UomConversionFactor = line.UomConversionFactor,
                UnitCost = line.UnitCostBilled, OriginalPurchasedQty = b.Invoiced, MaxReturnableQty = b.Invoiced - b.PriorCorrected });
        }
        if (note.Lines.Count == 0) throw new InvalidOperationException("This invoice has no remaining adjustable lines.");
        db.DebitNotes.Add(note); await db.SaveChangesAsync(); return note;
    }

    public static async Task<bool> IsPostedSalesAsync(AppDbContext db, SalesInvoice invoice) =>
        (invoice.Status == SalesInvoiceStatus.Posted || invoice.Status == SalesInvoiceStatus.InReview)
        && invoice.GLBatchId.HasValue && await db.GLBatches.AnyAsync(b => b.Id == invoice.GLBatchId.Value && b.CompanyId == invoice.CompanyId && b.Status == BatchStatus.Posted);
    public static Task<bool> IsPostedPurchaseAsync(AppDbContext db, PurchaseInvoice invoice) =>
        db.GLBatches.AnyAsync(b => invoice.IsPosted && invoice.GLBatchId.HasValue && b.Id == invoice.GLBatchId.Value && b.CompanyId == invoice.CompanyId && b.Status == BatchStatus.Posted);

    public static async Task EnrichCreditAsync(AppDbContext db, CreditNote note)
    {
        var invoice = await db.SalesInvoices.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == note.SalesInvoiceId);
        var prices = await InvoiceCorrectionPricing.SalesAsync(db, invoice);
        var sources = invoice.Lines.ToDictionary(x => x.Id);
        foreach (var line in note.Lines)
            if (line.SalesInvoiceLineId.HasValue && sources.TryGetValue(line.SalesInvoiceLineId.Value, out var source))
            { var b = await InvoiceCorrectionBalances.SalesAsync(db, source, note.Id); line.ReversalDiscountRate = prices[source.Id].DiscountRate; line.ReversalTaxRate = prices[source.Id].TaxRate; line.ReversalDiscountValue = prices[source.Id].Discount; line.ReversalTaxValue = prices[source.Id].Tax; line.PreviouslyReversedQty = b.PriorCorrected; line.OriginalSoldQty = b.Invoiced; line.MaxReturnableQty = Math.Max(0, b.Invoiced - b.PriorCorrected); }
    }
    public static async Task EnrichDebitAsync(AppDbContext db, DebitNote note)
    {
        var invoice = await db.PurchaseInvoices.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == note.PurchaseInvoiceId);
        var prices = await InvoiceCorrectionPricing.PurchaseAsync(db, invoice);
        var sources = invoice.Lines.ToDictionary(x => x.Id);
        foreach (var line in note.Lines)
            if (line.PurchaseInvoiceLineId.HasValue && sources.TryGetValue(line.PurchaseInvoiceLineId.Value, out var source))
            { var b = await InvoiceCorrectionBalances.PurchaseAsync(db, source, note.Id); line.ReversalDiscountRate = prices[source.Id].DiscountRate; line.ReversalTaxRate = prices[source.Id].TaxRate; line.ReversalDiscountValue = prices[source.Id].Discount; line.ReversalTaxValue = prices[source.Id].Tax; line.PreviouslyReversedQty = b.PriorCorrected; line.OriginalPurchasedQty = b.Invoiced; line.MaxReturnableQty = Math.Max(0, b.Invoiced - b.PriorCorrected); }
    }

    public async Task<string> SaveCreditAsync(CreditNote input)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var note = await db.CreditNotes.FromSqlInterpolated($"SELECT * FROM [CreditNotes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {input.Id}")
                .Include(x => x.Lines).FirstOrDefaultAsync();
            if (note?.SalesInvoiceId == null) return "Invoice credit note not found.";
            if (!note.ReturnToStock) return "Credit notes reverse invoice quantities. Use Receipt Refund for customer cash refunds.";
            if (note.Status == CreditNoteStatus.Void) return "A void credit note cannot be edited.";
            var batch = note.GlBatchId.HasValue ? await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == note.GlBatchId && x.CompanyId == note.CompanyId) : null;
            if (batch?.Status is BatchStatus.Ready or BatchStatus.Posted || (note.Status == CreditNoteStatus.Posted && batch == null)) return "This credit note is already submitted or posted.";
            var invoice = await db.SalesInvoices.FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {note.SalesInvoiceId.Value}").Include(x => x.Lines).FirstOrDefaultAsync();
            if (invoice == null || invoice.CompanyId != note.CompanyId || invoice.CustomerId != note.CustomerId || invoice.CurrencyId != note.CurrencyId || !await IsPostedSalesAsync(db, invoice)) return "The source invoice is unavailable or not posted.";
            var ids = invoice.Lines.Select(x => x.Id).ToList();
            await db.SalesInvoiceLines.FromSqlRaw("SELECT * FROM [SalesInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var rootIds = invoice.Lines.Where(x => x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).Distinct().OrderBy(x => x).ToList();
            await db.SalesOrderLines.FromSqlRaw("SELECT * FROM [SalesOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => rootIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            if (input.Lines.Count != note.Lines.Count || input.Lines.Select(x => x.Id).Distinct().Count() != input.Lines.Count) return "The invoice correction lines have changed. Reload the note.";
            var prices = await InvoiceCorrectionPricing.SalesAsync(db, invoice);
            var sources = invoice.Lines.ToDictionary(x => x.Id); var rootReductions = new Dictionary<Guid, decimal>(); decimal grossAdjusted = 0, discountAdjusted = 0, taxForeign = 0;
            var invoiceGross = invoice.Lines.Sum(x => x.Amount);
            var discount = invoice.DiscountPercentage > 0 ? invoiceGross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, invoiceGross);
            foreach (var line in note.Lines)
            {
                var submitted = input.Lines.SingleOrDefault(x => x.Id == line.Id);
                if (submitted == null || !line.SalesInvoiceLineId.HasValue || !sources.TryGetValue(line.SalesInvoiceLineId.Value, out var source)
                    || submitted.SalesInvoiceLineId != line.SalesInvoiceLineId || submitted.ItemId != source.ItemId || submitted.SalesOrderLineId != source.SalesOrderLineId
                    || submitted.UnitPrice != source.UnitPrice || submitted.UomId != source.UomId || submitted.UomConversionFactor != source.UomConversionFactor || submitted.Quantity < 0)
                    return "Each correction line must match its invoice item, price and unit.";
                var b = await InvoiceCorrectionBalances.SalesAsync(db, source, note.Id);
                if (submitted.Quantity > b.Invoiced - b.PriorCorrected) return $"The correction for '{source.Description}' exceeds its remaining invoice quantity.";
                var invoiceUnfulfilled = Math.Max(0, b.Invoiced - b.Fulfilled - b.PriorUnfulfilledCorrection);
                var unfulfilled = Math.Min(invoiceUnfulfilled, b.RootUnfulfilled);
                var reduce = Math.Min(submitted.Quantity, unfulfilled);
                if (reduce > Math.Max(0, unfulfilled - b.Reserved)) return $"The correction for '{source.Description}' conflicts with a shipment draft.";
                if (source.SalesOrderLineId.HasValue) { var root = source.SalesOrderLineId.Value; rootReductions[root] = rootReductions.GetValueOrDefault(root) + reduce; if (rootReductions[root] > Math.Max(0, b.RootUnfulfilled - b.Reserved)) return "Combined corrections exceed the remaining order quantity."; }
                line.DimensionSetId = submitted.DimensionSetId; line.Quantity = submitted.Quantity; line.UnfulfilledQuantity = reduce;
                var amount = prices[source.Id].ForQuantity(b.PriorCorrected, line.Quantity, b.Invoiced);
                grossAdjusted += amount.Gross; discountAdjusted += amount.Discount; taxForeign += amount.Tax;
            }
            note.TotalAmount = Math.Round(grossAdjusted - discountAdjusted + taxForeign, 2, MidpointRounding.AwayFromZero);
            note.Date = input.Date; note.TransactionDateTime = input.TransactionDateTime; note.Reason = input.Reason;
            note.OverrideRevenueGlAccountId = input.OverrideRevenueGlAccountId; note.OverrideReceivablesGlAccountId = input.OverrideReceivablesGlAccountId; note.CustomTransactionTypeId = input.CustomTransactionTypeId;
            if (batch?.Status == BatchStatus.Rejected) { note.Status = CreditNoteStatus.Draft; note.GlBatchId = null; }
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
        catch (DbUpdateException) { return "The credit note could not be saved. Reload and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice or source quantity is busy. Reload and try again."; }
    }

    public async Task<string> SaveDebitAsync(DebitNote input)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var note = await db.DebitNotes.FromSqlInterpolated($"SELECT * FROM [DebitNotes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {input.Id}").Include(x => x.Lines).FirstOrDefaultAsync();
            if (note?.PurchaseInvoiceId == null) return "Invoice debit note not found.";
            if (note.Status == DebitNoteStatus.Void) return "A void debit note cannot be edited.";
            var batch = note.GlBatchId.HasValue ? await db.GLBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == note.GlBatchId && x.CompanyId == note.CompanyId) : null;
            if (batch?.Status is BatchStatus.Ready or BatchStatus.Posted || (note.Status == DebitNoteStatus.Posted && batch == null)) return "This debit note is already submitted or posted.";
            var invoice = await db.PurchaseInvoices.FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {note.PurchaseInvoiceId.Value}").Include(x => x.Lines).FirstOrDefaultAsync();
            if (invoice == null || invoice.CompanyId != note.CompanyId || invoice.VendorId != note.VendorId || invoice.CurrencyId != note.CurrencyId || !await IsPostedPurchaseAsync(db, invoice)) return "The source invoice is unavailable or not posted.";
            var ids = invoice.Lines.Select(x => x.Id).ToList();
            await db.PurchaseInvoiceLines.FromSqlRaw("SELECT * FROM [PurchaseInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var roots = invoice.Lines.Where(x => x.PurchaseOrderLineId.HasValue).Select(x => x.PurchaseOrderLineId!.Value).Distinct().OrderBy(x => x).ToList();
            await db.PurchaseOrderLines.FromSqlRaw("SELECT * FROM [PurchaseOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => roots.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            if (input.Lines.Count != note.Lines.Count || input.Lines.Select(x => x.Id).Distinct().Count() != input.Lines.Count) return "The invoice correction lines have changed. Reload the note.";
            var prices = await InvoiceCorrectionPricing.PurchaseAsync(db, invoice);
            var sources = invoice.Lines.ToDictionary(x => x.Id); var rootReductions = new Dictionary<Guid, decimal>(); decimal grossAdjusted = 0, discountAdjusted = 0, taxAdjusted = 0;
            var invoiceGross = invoice.Lines.Sum(x => x.LineTotal);
            var discount = invoice.DiscountPercentage > 0 ? invoiceGross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, invoiceGross);
            foreach (var line in note.Lines)
            {
                var submitted = input.Lines.SingleOrDefault(x => x.Id == line.Id);
                if (submitted == null || !line.PurchaseInvoiceLineId.HasValue || !sources.TryGetValue(line.PurchaseInvoiceLineId.Value, out var source)
                    || submitted.PurchaseInvoiceLineId != line.PurchaseInvoiceLineId || submitted.ItemId != source.ItemId || submitted.PurchaseOrderLineId != source.PurchaseOrderLineId
                    || submitted.UnitCost != source.UnitCostBilled || submitted.UomId != source.UomId || submitted.UomConversionFactor != source.UomConversionFactor || submitted.Quantity < 0)
                    return "Each correction line must match its invoice item, cost and unit.";
                var b = await InvoiceCorrectionBalances.PurchaseAsync(db, source, note.Id);
                if (submitted.Quantity > b.Invoiced - b.PriorCorrected) return $"The correction for '{source.Description}' exceeds its remaining invoice quantity.";
                var invoiceUnfulfilled = Math.Max(0, b.Invoiced - b.Fulfilled - b.PriorUnfulfilledCorrection);
                var unfulfilled = Math.Min(invoiceUnfulfilled, b.RootUnfulfilled); var reduce = Math.Min(submitted.Quantity, unfulfilled);
                if (reduce > Math.Max(0, unfulfilled - b.Reserved)) return $"The correction for '{source.Description}' conflicts with a receipt draft.";
                if (source.PurchaseOrderLineId.HasValue) { var root = source.PurchaseOrderLineId.Value; rootReductions[root] = rootReductions.GetValueOrDefault(root) + reduce; if (rootReductions[root] > Math.Max(0, b.RootUnfulfilled - b.Reserved)) return "Combined corrections exceed the remaining order quantity."; }
                line.DimensionSetId = submitted.DimensionSetId; line.Quantity = submitted.Quantity; line.UnfulfilledQuantity = reduce;
                var amount = prices[source.Id].ForQuantity(b.PriorCorrected, line.Quantity, b.Invoiced);
                grossAdjusted += amount.Gross; discountAdjusted += amount.Discount; taxAdjusted += amount.Tax;
            }
            var net = grossAdjusted - discountAdjusted;
            note.TotalAmount = Math.Round(net + taxAdjusted, 2, MidpointRounding.AwayFromZero);
            note.Date = input.Date; note.TransactionDateTime = input.TransactionDateTime; note.Reason = input.Reason;
            note.OverrideAccountsPayableGlAccountId = input.OverrideAccountsPayableGlAccountId; note.OverrideGrIrClearingGlAccountId = input.OverrideGrIrClearingGlAccountId; note.CustomTransactionTypeId = input.CustomTransactionTypeId;
            if (batch?.Status == BatchStatus.Rejected) { note.Status = DebitNoteStatus.Draft; note.GlBatchId = null; }
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
        catch (DbUpdateException) { return "The debit note could not be saved. Reload and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice or source quantity is busy. Reload and try again."; }
    }

    public async Task<string> PostCreditAsync(Guid id, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var note = await db.CreditNotes.FromSqlInterpolated($"SELECT * FROM [CreditNotes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").Include(x => x.Lines).FirstOrDefaultAsync();
            if (note?.SalesInvoiceId == null) return "Invoice credit note not found.";
            if (!note.ReturnToStock) return "Credit notes reverse invoice quantities. Use Receipt Refund for customer cash refunds.";
            if (note.Status != CreditNoteStatus.Draft || note.GlBatchId.HasValue) return "This credit note is already submitted or locked.";
            var invoice = await db.SalesInvoices.FromSqlInterpolated($"SELECT * FROM [SalesInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {note.SalesInvoiceId.Value}").Include(x => x.Lines).FirstOrDefaultAsync();
            if (invoice == null || invoice.CompanyId != note.CompanyId || invoice.CustomerId != note.CustomerId || invoice.CurrencyId != note.CurrencyId || !await IsPostedSalesAsync(db, invoice)) return "The source invoice is unavailable or not posted.";
            var ids = invoice.Lines.Select(x => x.Id).ToList();
            await db.SalesInvoiceLines.FromSqlRaw("SELECT * FROM [SalesInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var roots = invoice.Lines.Where(x => x.SalesOrderLineId.HasValue).Select(x => x.SalesOrderLineId!.Value).Distinct().OrderBy(x => x).ToList();
            await db.SalesOrderLines.FromSqlRaw("SELECT * FROM [SalesOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => roots.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var selected = note.Lines.Where(x => x.Quantity > 0).ToList();
            if (note.Lines.Any(x => x.Quantity < 0) || selected.Count == 0 || selected.Any(x => !x.SalesInvoiceLineId.HasValue)
                || selected.Select(x => x.SalesInvoiceLineId).Distinct().Count() != selected.Count) return "Select distinct invoice lines with positive quantities.";
            var prices = await InvoiceCorrectionPricing.SalesAsync(db, invoice);
            var sources = invoice.Lines.ToDictionary(x => x.Id); var rootReductions = new Dictionary<Guid, decimal>();
            var ledger = await db.GLTransactions.AsNoTracking().Where(x => x.CompanyId == note.CompanyId && x.BatchId == invoice.GLBatchId).ToListAsync();
            var arAccounts = ledger.Where(x => x.Debit > 0 && x.Narration == $"AR {invoice.InvoiceNumber}").Select(x => x.SegCoaId).Distinct().ToList();
            var ar = note.OverrideReceivablesGlAccountId ?? (arAccounts.Count == 1 ? arAccounts[0] : Guid.Empty);
            if (ar == Guid.Empty) return "The original invoice receivables account could not be verified.";
            var invoiceGross = invoice.Lines.Sum(x => x.Amount);
            var invoiceDiscount = invoice.DiscountPercentage > 0 ? invoiceGross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, invoiceGross);
            var glLines = new List<GLJournalLine>(); decimal grossForeign = 0, grossBase = 0, discountForeign = 0, discountBase = 0, taxForeign = 0, taxBase = 0;
            foreach (var line in selected)
            {
                if (!sources.TryGetValue(line.SalesInvoiceLineId!.Value, out var source) || source.ItemId != line.ItemId || source.SalesOrderLineId != line.SalesOrderLineId
                    || source.UnitPrice != line.UnitPrice || source.UomId != line.UomId || source.UomConversionFactor != line.UomConversionFactor) return "A correction line no longer matches its invoice.";
                var b = await InvoiceCorrectionBalances.SalesAsync(db, source, note.Id);
                if (line.Quantity > b.Invoiced - b.PriorCorrected) return $"The correction for '{source.Description}' exceeds its remaining invoice quantity.";
                var unfulfilled = Math.Min(Math.Max(0, b.Invoiced - b.Fulfilled - b.PriorUnfulfilledCorrection), b.RootUnfulfilled);
                var reduction = Math.Min(line.Quantity, unfulfilled);
                if (reduction > Math.Max(0, unfulfilled - b.Reserved)) return $"The correction for '{source.Description}' conflicts with a shipment draft.";
                if (source.SalesOrderLineId.HasValue) { var root = source.SalesOrderLineId.Value; rootReductions[root] = rootReductions.GetValueOrDefault(root) + reduction; if (rootReductions[root] > Math.Max(0, b.RootUnfulfilled - b.Reserved)) return "Combined corrections exceed the remaining order quantity."; }
                line.UnfulfilledQuantity = reduction;
                var postedGross = Math.Round(source.Amount * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                var reference = $"Sales {invoice.InvoiceNumber}: {source.Description ?? "Direct line"}";
                var postedAccounts = ledger.Where(x => x.Credit == postedGross && x.Narration == reference).Select(x => x.SegCoaId).Distinct().ToList();
                var account = note.OverrideRevenueGlAccountId ?? (postedAccounts.Count == 1 ? postedAccounts[0] : Guid.Empty);
                if (account == Guid.Empty) return $"The original revenue account for '{source.Description}' could not be verified.";
                var amount = prices[source.Id].ForQuantity(b.PriorCorrected, line.Quantity, b.Invoiced);
                var gross = amount.Gross;
                var baseAmount = amount.GrossBase;
                if (baseAmount > 0) glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = account, Debit = baseAmount, Reference = $"Credit: {source.Description}" });
                grossForeign += gross; grossBase += baseAmount;
                discountForeign += amount.Discount; discountBase += amount.DiscountBase;
                var price = prices[source.Id];
                var tf = amount.Tax;
                var tb = amount.TaxBase;
                taxForeign += tf; taxBase += tb;
                if (tb > 0 && (!price.TaxAccountId.HasValue || price.TaxAccountId == Guid.Empty)) return "The original invoice tax account is unavailable.";
                if (tb > 0) glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = price.TaxAccountId!.Value, Debit = tb, Reference = $"Tax reversal: {source.Description}" });
            }
            if (discountBase > 0)
            {
                var accounts = ledger.Where(x => x.Debit > 0 && x.Narration == $"Discount {invoice.InvoiceNumber}").Select(x => x.SegCoaId).Distinct().ToList();
                if (accounts.Count != 1) return "The original invoice discount account could not be verified.";
                glLines.Add(new GLJournalLine { SegCoaId = accounts[0], Credit = discountBase, Reference = $"Discount reversal: {invoice.InvoiceNumber}" });
            }
            var totalBase = grossBase - discountBase + taxBase;
            if (totalBase <= 0) return "The correction must have a positive accounting value.";
            glLines.Add(new GLJournalLine { SegCoaId = ar, Credit = totalBase, Reference = $"AR adjustment: {invoice.InvoiceNumber}" });
            var (error, batchId) = await gl.StageNewSubledgerBatchAsync(db, note.CompanyId, note.Date, "Credit Note", note.CreditNoteNumber, note.CreditNoteNumber, glLines, userId.ToString(), invoice.ProjectId);
            if (!string.IsNullOrEmpty(error) || !batchId.HasValue) return $"Unable to submit credit note: {error}";
            note.TotalAmount = Math.Round(grossForeign - discountForeign + taxForeign, 2, MidpointRounding.AwayFromZero);
            note.GlBatchId = batchId; note.Status = CreditNoteStatus.Posted; note.PostedAt = DateTime.UtcNow; note.PostedByUserId = userId;
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
        catch (DbUpdateException) { return "The credit note could not be submitted. Reload and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice or source quantity is busy. Reload and try again."; }
    }

    public async Task<string> PostDebitAsync(Guid id, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var note = await db.DebitNotes.FromSqlInterpolated($"SELECT * FROM [DebitNotes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").Include(x => x.Lines).FirstOrDefaultAsync();
            if (note?.PurchaseInvoiceId == null) return "Invoice debit note not found.";
            if (note.Status != DebitNoteStatus.Draft || note.GlBatchId.HasValue) return "This debit note is already submitted or locked.";
            var invoice = await db.PurchaseInvoices.FromSqlInterpolated($"SELECT * FROM [PurchaseInvoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {note.PurchaseInvoiceId.Value}").Include(x => x.Lines).Include(x => x.Vendor).FirstOrDefaultAsync();
            if (invoice == null || invoice.CompanyId != note.CompanyId || invoice.VendorId != note.VendorId || invoice.CurrencyId != note.CurrencyId || !await IsPostedPurchaseAsync(db, invoice)) return "The source invoice is unavailable or not posted.";
            var ids = invoice.Lines.Select(x => x.Id).ToList();
            await db.PurchaseInvoiceLines.FromSqlRaw("SELECT * FROM [PurchaseInvoiceLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var roots = invoice.Lines.Where(x => x.PurchaseOrderLineId.HasValue).Select(x => x.PurchaseOrderLineId!.Value).Distinct().OrderBy(x => x).ToList();
            await db.PurchaseOrderLines.FromSqlRaw("SELECT * FROM [PurchaseOrderLines] WITH (UPDLOCK, HOLDLOCK)").Where(x => roots.Contains(x.Id)).Select(x => x.Id).ToListAsync();
            var selected = note.Lines.Where(x => x.Quantity > 0).ToList();
            if (note.Lines.Any(x => x.Quantity < 0) || selected.Count == 0 || selected.Any(x => !x.PurchaseInvoiceLineId.HasValue)
                || selected.Select(x => x.PurchaseInvoiceLineId).Distinct().Count() != selected.Count) return "Select distinct invoice lines with positive quantities.";
            var prices = await InvoiceCorrectionPricing.PurchaseAsync(db, invoice);
            var sources = invoice.Lines.ToDictionary(x => x.Id); var rootReductions = new Dictionary<Guid, decimal>();
            var ledger = await db.GLTransactions.AsNoTracking().Where(x => x.CompanyId == note.CompanyId && x.BatchId == invoice.GLBatchId).ToListAsync();
            var apAccounts = ledger.Where(x => x.Credit > 0 && x.Narration == $"Purchase Invoice {invoice.PurchaseInvoiceNumber} - {invoice.Vendor?.Name ?? "Unknown"}").Select(x => x.SegCoaId).Distinct().ToList();
            var ap = note.OverrideAccountsPayableGlAccountId ?? (apAccounts.Count == 1 ? apAccounts[0] : Guid.Empty);
            if (ap == Guid.Empty) return "The original invoice payable account could not be verified.";
            var invoiceGross = invoice.Lines.Sum(x => x.LineTotal);
            var invoiceDiscount = invoice.DiscountPercentage > 0 ? invoiceGross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, invoiceGross);
            var glLines = new List<GLJournalLine>(); decimal grossForeign = 0, grossBase = 0, discountForeign = 0, discountBase = 0, taxForeign = 0, taxBase = 0;
            foreach (var line in selected)
            {
                if (!sources.TryGetValue(line.PurchaseInvoiceLineId!.Value, out var source) || source.ItemId != line.ItemId || source.PurchaseOrderLineId != line.PurchaseOrderLineId
                    || source.UnitCostBilled != line.UnitCost || source.UomId != line.UomId || source.UomConversionFactor != line.UomConversionFactor) return "A correction line no longer matches its invoice.";
                var b = await InvoiceCorrectionBalances.PurchaseAsync(db, source, note.Id);
                if (line.Quantity > b.Invoiced - b.PriorCorrected) return $"The correction for '{source.Description}' exceeds its remaining invoice quantity.";
                var unfulfilled = Math.Min(Math.Max(0, b.Invoiced - b.Fulfilled - b.PriorUnfulfilledCorrection), b.RootUnfulfilled);
                var reduction = Math.Min(line.Quantity, unfulfilled);
                if (reduction > Math.Max(0, unfulfilled - b.Reserved)) return $"The correction for '{source.Description}' conflicts with a receipt draft.";
                if (source.PurchaseOrderLineId.HasValue) { var root = source.PurchaseOrderLineId.Value; rootReductions[root] = rootReductions.GetValueOrDefault(root) + reduction; if (rootReductions[root] > Math.Max(0, b.RootUnfulfilled - b.Reserved)) return "Combined corrections exceed the remaining order quantity."; }
                line.UnfulfilledQuantity = reduction;
                var postedGross = Math.Round(source.LineTotal * invoice.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                var reference = !string.IsNullOrWhiteSpace(source.Description) ? source.Description : $"Purchase Invoice: {invoice.ExternalInvoiceNumber}";
                var postedAccounts = ledger.Where(x => x.Debit == postedGross && x.Narration == reference).Select(x => x.SegCoaId).Distinct().ToList();
                var account = note.OverrideGrIrClearingGlAccountId ?? (postedAccounts.Count == 1 ? postedAccounts[0] : Guid.Empty);
                if (account == Guid.Empty) return $"The original invoice account for '{source.Description}' could not be verified.";
                var amount = prices[source.Id].ForQuantity(b.PriorCorrected, line.Quantity, b.Invoiced);
                var gross = amount.Gross;
                var baseAmount = amount.GrossBase;
                discountForeign += amount.Discount; discountBase += amount.DiscountBase;
                taxForeign += amount.Tax; taxBase += amount.TaxBase;
                if (baseAmount > 0) glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = account, Credit = baseAmount, Reference = $"Debit note: {source.Description}" });
                grossForeign += gross; grossBase += baseAmount;
            }
            if (discountBase > 0)
            {
                var accounts = ledger.Where(x => x.Credit > 0 && x.Narration == $"Discount {invoice.PurchaseInvoiceNumber}").Select(x => x.SegCoaId).Distinct().ToList();
                if (accounts.Count != 1) return "The original invoice discount account could not be verified.";
                glLines.Add(new GLJournalLine { SegCoaId = accounts[0], Debit = discountBase, Reference = $"Discount reversal: {invoice.PurchaseInvoiceNumber}" });
            }
            var originalTax = prices.Values.FirstOrDefault();
            if (originalTax != null && taxBase > 0)
            {
                if (taxBase > 0 && (!originalTax.TaxAccountId.HasValue || originalTax.TaxAccountId == Guid.Empty)) return "The original invoice tax account is unavailable.";
                if (taxBase > 0) glLines.Add(new GLJournalLine { SegCoaId = originalTax.TaxAccountId!.Value, Credit = taxBase, Reference = $"Tax reversal: {invoice.PurchaseInvoiceNumber}" });
            }
            var totalBase = grossBase - discountBase + taxBase;
            if (totalBase <= 0) return "The correction must have a positive accounting value.";
            glLines.Add(new GLJournalLine { SegCoaId = ap, Debit = totalBase, Reference = $"AP adjustment: {invoice.PurchaseInvoiceNumber}" });
            var (error, batchId) = await gl.StageNewSubledgerBatchAsync(db, note.CompanyId, note.Date, "Debit Note", note.DebitNoteNumber, note.DebitNoteNumber, glLines, userId.ToString(), invoice.ProjectId);
            if (!string.IsNullOrEmpty(error) || !batchId.HasValue) return $"Unable to submit debit note: {error}";
            note.TotalAmount = Math.Round(grossForeign - discountForeign + taxForeign, 2, MidpointRounding.AwayFromZero);
            note.GlBatchId = batchId; note.Status = DebitNoteStatus.Posted; note.PostedAt = DateTime.UtcNow; note.PostedByUserId = userId;
            await db.SaveChangesAsync(); await tx.CommitAsync(); return string.Empty;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
        catch (DbUpdateException) { return "The debit note could not be submitted. Reload and try again."; }
        catch (Microsoft.Data.SqlClient.SqlException) { return "The invoice or source quantity is busy. Reload and try again."; }
    }
}
