using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class CreditNoteService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly InventoryService _invService;
        private readonly TransactionMappingService _mappingService;
        private readonly IDocumentNumberingService _numberingService;

        public CreditNoteService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            InventoryService invService,
            TransactionMappingService mappingService,
            IDocumentNumberingService numberingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService;
            _numberingService = numberingService;
        }

        public async Task<List<SalesOrder>> GetInvoicesEligibleForAdjustmentAsync(Guid companyId, Guid customerId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var validStatuses = new[] { OrderStatus.Invoiced, OrderStatus.PartiallyInvoiced };

            var invoices = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId
                         && o.CustomerId == customerId
                         && (o.IsDirectInvoice || o.InvoiceBatchId != null || o.OrderNumber.StartsWith("INV"))
                         && validStatuses.Contains(o.Status)
                         && o.Lines.Any(l => l.Item != null && !l.Item.IsService))
                .ToListAsync();

            var invoiceIds = invoices.Select(o => o.Id).ToList();

            var historicalCreditsMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => (cnl.Header!.SalesOrderId.HasValue && invoiceIds.Contains(cnl.Header.SalesOrderId.Value))
                           && cnl.Header.Status != CreditNoteStatus.Void)
                .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var eligibleInvoices = new List<SalesOrder>();
            foreach (var inv in invoices)
            {
                bool hasAdjustableQuantities = false;

                foreach (var line in inv.Lines)
                {
                    if (line.Item == null || line.Item.IsService) continue;

                    decimal alreadyCredited = historicalCreditsMap.TryGetValue(line.Id, out var creditedQty) ? creditedQty : 0;

                    if (line.Quantity - alreadyCredited > 0)
                    {
                        hasAdjustableQuantities = true;
                        break;
                    }
                }

                if (hasAdjustableQuantities)
                {
                    eligibleInvoices.Add(inv);
                }
            }

            var documents = await ctx.SalesInvoices.AsNoTracking().Include(x => x.Lines)
                .Where(x => x.CompanyId == companyId && x.CustomerId == customerId && !x.InvoiceNumber.StartsWith("AR-LEGACY-") && (x.Status == SalesInvoiceStatus.Posted || x.Status == SalesInvoiceStatus.InReview)
                    && x.GLBatchId.HasValue && ctx.GLBatches.Any(b => b.Id == x.GLBatchId.Value && b.CompanyId == companyId && b.Status == BatchStatus.Posted)).ToListAsync();
            foreach (var doc in documents)
                if (await HasAdjustableSalesLineAsync(ctx, doc)) eligibleInvoices.Add(new SalesOrder { Id = doc.Id, CompanyId = doc.CompanyId, CustomerId = doc.CustomerId,
                    OrderNumber = doc.InvoiceNumber, Date = doc.Date, CurrencyId = doc.CurrencyId, ExchangeRate = doc.ExchangeRate });
            return eligibleInvoices.OrderByDescending(x => x.Date).ToList();
        }

        private static async Task<bool> HasAdjustableSalesLineAsync(AppDbContext db, SalesInvoice invoice)
        {
            foreach (var line in invoice.Lines) { var balance = await InvoiceCorrectionBalances.SalesAsync(db, line); if (balance.Invoiced > balance.PriorCorrected) return true; }
            return false;
        }

        public Task<List<SalesOrder>> GetPaidInvoicesByCustomerAsync(Guid companyId, Guid customerId) => Task.FromResult(new List<SalesOrder>());

        public async Task<CreditNote> CreateStockReturnDraftAsync(Guid orderId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (await ctx.SalesInvoices.AnyAsync(x => x.Id == orderId && !x.InvoiceNumber.StartsWith("AR-LEGACY-"))) return await new NewInvoiceCorrectionService(_dbFactory, _glOps, _numberingService).CreateCreditAsync(orderId, userId);
            var so = await ctx.SalesOrders
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == orderId);

            if (so == null) throw new Exception("Target sales invoice reference missing.");

            var previousReturns = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status != CreditNoteStatus.Void)
                .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var creditNote = new CreditNote
            {
                Id = Guid.NewGuid(),
                CompanyId = so.CompanyId,
                SalesOrderId = so.Id,
                ProjectId = so.ProjectId,
                CustomerId = so.CustomerId,
                CurrencyId = so.CurrencyId,
                ExchangeRate = so.ExchangeRate,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = CreditNoteStatus.Draft,
                Reason = "Pre-Shipment Invoice Quantity Correction",
                ReturnToStock = true,
                WarehouseId = so.WarehouseId,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                CreditNoteNumber = await _numberingService.GenerateAndReserveNextNumberAsync(so.CompanyId, SequenceTargetType.AR_CreditNote)
            };

            foreach (var soLine in so.Lines)
            {
                if (soLine.Item != null && soLine.Item.IsService) continue;

                decimal alreadyCredited = previousReturns.TryGetValue(soLine.Id, out var creditedQty) ? creditedQty : 0;
                decimal maxAdjustable = soLine.Quantity - alreadyCredited;

                if (maxAdjustable > 0)
                {
                    creditNote.Lines.Add(new CreditNoteLine
                    { DimensionSetId = soLine.DimensionSetId,
                        Id = Guid.NewGuid(),
                        HeaderId = creditNote.Id,
                        ItemId = soLine.ItemId ?? Guid.Empty,
                        UomId = soLine.UomId,
                        UomName = soLine.UomName,
                        UomConversionFactor = soLine.UomConversionFactor,
                        SalesOrderLineId = soLine.Id,
                        Quantity = 0,
                        UnitPrice = soLine.UnitPrice,
                        OriginalSoldQty = soLine.Quantity,
                        MaxReturnableQty = maxAdjustable
                    });
                }
            }

            if (!creditNote.Lines.Any()) throw new Exception("This invoice has already been completely cleared by previous credit notes.");

            ctx.CreditNotes.Add(creditNote);
            await ctx.SaveChangesAsync();
            return creditNote;
        }

        public async Task<CreditNote?> GetByIdAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var cn = await ctx.CreditNotes
                .Include(c => c.Lines).ThenInclude(l => l.Item)
                .Include(c => c.Customer)
                .Include(c => c.SalesOrder).ThenInclude(o => o.Lines)
                .Include(c => c.SalesInvoice).Include(c => c.Lines).ThenInclude(l => l.SalesInvoiceLine)
                .Include(c => c.Currency)
                .Include(c => c.Warehouse)
                .Include(c => c.CustomTransactionType)
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);

            if (cn?.SalesInvoiceId != null) await NewInvoiceCorrectionService.EnrichCreditAsync(ctx, cn);
            if (cn != null && cn.SalesOrder != null)
            {
                var historicalReturnsMap = await ctx.CreditNoteLines
                    .Include(cnl => cnl.Header)
                    .Where(cnl => cnl.Header!.SalesOrderId == cn.SalesOrderId
                               && cnl.Header.Id != cn.Id
                               && cnl.Header.Status != CreditNoteStatus.Void)
                    .Where(cnl => cnl.SalesOrderLineId.HasValue).GroupBy(cnl => cnl.SalesOrderLineId!.Value)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                var gross = cn.SalesOrder.Lines.Sum(x => x.Quantity * x.UnitPrice);
                var discountRate = cn.SalesOrder.DiscountPercentage > 0 ? cn.SalesOrder.DiscountPercentage / 100m : (gross > 0 ? Math.Min(cn.SalesOrder.DiscountAmount, gross) / gross : 0);
                var taxRate = cn.SalesOrder.TaxId.HasValue ? await ctx.Taxes.Where(x => x.Id == cn.SalesOrder.TaxId && x.CompanyId == cn.CompanyId).Select(x => (decimal?)x.Per).FirstOrDefaultAsync() ?? 0 : 0;
                foreach (var line in cn.Lines)
                {
                    line.ReversalDiscountRate = discountRate; line.ReversalTaxRate = (1 - discountRate) * taxRate / 100m;
                    var matchingInvoiceLine = cn.SalesOrder.Lines.FirstOrDefault(sol => sol.Id == line.SalesOrderLineId);
                    if (matchingInvoiceLine != null)
                    {
                        line.OriginalSoldQty = matchingInvoiceLine.Quantity;
                        decimal previouslyCredited = historicalReturnsMap.TryGetValue(line.SalesOrderLineId.GetValueOrDefault(), out var creditedQty) ? creditedQty : 0;
                        line.MaxReturnableQty = matchingInvoiceLine.Quantity - previouslyCredited;
                    }
                }
            }

            return cn;
        }

        public async Task<string> PostStockCreditNoteAsync(Guid cnId, Guid userId)
        {
            await using (var probe = await _dbFactory.CreateDbContextAsync())
                if (await probe.CreditNotes.AnyAsync(x => x.Id == cnId && x.SalesInvoiceId.HasValue)) return await new NewInvoiceCorrectionService(_dbFactory, _glOps, _numberingService).PostCreditAsync(cnId, userId);
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();
            try
            {
                var cn = await ctx.CreditNotes
                    .FromSqlInterpolated($"SELECT * FROM [CreditNotes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {cnId}")
                    .Include(c => c.Lines).ThenInclude(l => l.Item)
                    .Include(c => c.Customer)
                    .Include(c => c.SalesOrder).ThenInclude(so => so.Lines)
                    .FirstOrDefaultAsync(c => c.Id == cnId);

                if (cn == null) return "Credit note execution layout parameters not found.";
                if (!cn.ReturnToStock) return "Credit notes reverse invoice quantities. Use Receipt Refund for customer cash refunds.";
                if (cn.Status == CreditNoteStatus.Posted)
                {
                    var existingBatch = cn.GlBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == cn.GlBatchId.Value && b.CompanyId == cn.CompanyId)
                        : null;
                    if (existingBatch?.Status == BatchStatus.Posted) return "Document already committed to the General Ledger.";
                    if (existingBatch?.Status == BatchStatus.Ready) return "Document is already awaiting General Ledger review.";
                    if (existingBatch == null) return "Document is already locked and has no review batch.";
                }
                if (cn.SalesOrder == null) return "Parent invoice reference missing from transaction context.";

                var so = cn.SalesOrder;
                if (so.CompanyId != cn.CompanyId || so.CustomerId != cn.CustomerId || so.CurrencyId != cn.CurrencyId)
                    return "The credit note customer, currency and company must match its source.";
                await ctx.SalesOrders.FromSqlInterpolated($"SELECT * FROM [SalesOrders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {cn.SalesOrderId}").Select(x => x.Id).ToListAsync();
                var selected = cn.Lines.Where(x => x.Quantity > 0).ToList();
                if (cn.Lines.Any(x => x.Quantity < 0) || selected.Count == 0 || selected.Any(x => !x.SalesOrderLineId.HasValue) || selected.Where(x => x.SalesOrderLineId.HasValue).GroupBy(x => x.SalesOrderLineId!.Value).Any(x => x.Count() > 1))
                    return "Select distinct source lines with positive correction quantities.";
                var prior = await ctx.CreditNoteLines.Where(x => x.HeaderId != cn.Id && x.Header!.SalesOrderId == so.Id
                        && x.Header.ReturnToStock && x.Header.Status != CreditNoteStatus.Void)
                    .Where(x => x.SalesOrderLineId.HasValue).GroupBy(x => x.SalesOrderLineId!.Value).Select(x => new { Id = x.Key, Qty = x.Sum(y => y.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
                var sourceIds = selected.Select(x => x.SalesOrderLineId!.Value).ToList();
                await ctx.SalesOrderLines.FromSqlRaw("SELECT * FROM [SalesOrderLines] WITH (UPDLOCK, HOLDLOCK)")
                    .Where(x => sourceIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
                var dispatches = await ctx.SalesShipmentLines.Where(x => x.SalesOrderLineId.HasValue && sourceIds.Contains(x.SalesOrderLineId.Value) && x.Shipment!.CompanyId == cn.CompanyId
                        && (x.Shipment.Status == ShipmentStatus.Pending || x.Shipment.Status == ShipmentStatus.Shipped))
                    .Select(x => new { x.SalesOrderLineId, x.QtyShipped, x.Shipment!.Status }).ToListAsync();
                foreach (var line in selected)
                {
                    var source = so.Lines.FirstOrDefault(x => x.Id == line.SalesOrderLineId);
                    if (source == null || source.ItemId != line.ItemId || source.UnitPrice != line.UnitPrice)
                        return "Each credit line must match its source item and unit price.";
                    if (line.Quantity + prior.GetValueOrDefault(line.SalesOrderLineId!.Value) > source.Quantity)
                        return $"The correction for {line.Item?.Name ?? "this item"} exceeds its remaining invoice quantity.";
                    var shipped = Math.Max(source.QtyShipped, dispatches.Where(x => x.SalesOrderLineId == source.Id && x.Status == ShipmentStatus.Shipped).Sum(x => x.QtyShipped));
                    var reserved = dispatches.Where(x => x.SalesOrderLineId == source.Id && x.Status == ShipmentStatus.Pending).Sum(x => x.QtyShipped);
                    if (reserved > Math.Max(0m, source.Quantity - shipped - prior.GetValueOrDefault(source.Id) - line.Quantity))
                        return $"The correction for {line.Item?.Name ?? "this item"} conflicts with quantities reserved by shipment drafts.";
                }

                // 1. Resolve custom mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (cn.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == cn.CompanyId && m.CustomTransactionTypeId == cn.CustomTransactionTypeId.Value);
                }

                // 2. Resolve Discount GL Account
                bool hasDiscounts = so.DiscountPercentage > 0 || so.DiscountAmount > 0;
                Guid discountAccount = so.DiscountGlAccountId ?? Guid.Empty;

                if (hasDiscounts && discountAccount == Guid.Empty)
                {
                    discountAccount = await _mappingService.GetMappedAccountAsync(
                        cn.CompanyId,
                        SystemTransactionType.DiscountAllowed,
                        isDebit: true,
                        defaultAccountId: Guid.Empty);

                    if (discountAccount == Guid.Empty)
                        return "Posting Aborted: A discount is present on the invoice, but no valid Discount GL Account could be resolved.";
                }

                // 3. Resolve Tax Account
                decimal taxPer = 0;
                Guid taxGlAccountId = Guid.Empty;
                if (so.TaxId.HasValue)
                {
                    var taxDef = await ctx.Taxes.FindAsync(so.TaxId.Value);
                    if (taxDef != null && taxDef.Per > 0)
                    {
                        taxPer = taxDef.Per;
                        taxGlAccountId = so.TaxGLAccountId ?? taxDef.GLAccountId ?? Guid.Empty;
                        if (taxGlAccountId == Guid.Empty)
                            return $"Posting Aborted: Tax calculation rules apply ({taxDef.TaxCode}), but Tax GL Account mapping is missing.";
                    }
                }

                // 4. Resolve AR Account (Credit)
                Guid arAccount = cn.OverrideReceivablesGlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? Guid.Empty;

                if (arAccount == Guid.Empty)
                {
                    Guid defaultAr = cn.Customer?.ReceivablesAccountId ?? Guid.Empty;
                    arAccount = await _mappingService.GetMappedAccountAsync(
                        cn.CompanyId,
                        SystemTransactionType.CreditNote,
                        isDebit: false,
                        defaultAccountId: defaultAr);
                }

                if (arAccount == Guid.Empty)
                    return "Posting Aborted: Customer Accounts Receivable (AR) GL account mapping is unassigned.";

                var glLines = new List<GLJournalLine>();
                decimal totalArReductionBase = 0;
                decimal totalArReductionForeign = 0;
                decimal originalSubTotalForeign = so.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal rate = cn.ExchangeRate > 0 ? cn.ExchangeRate : 1;

                foreach (var line in cn.Lines)
                {
                    if (line.Quantity <= 0 || line.Item == null) continue;

                    // 5. Resolve Revenue Account (Debit)
                    Guid revenueAccount = cn.OverrideRevenueGlAccountId
                        ?? customMapping?.OverrideDebitGlAccountId
                        ?? Guid.Empty;

                    if (revenueAccount == Guid.Empty)
                    {
                        revenueAccount = await _mappingService.GetMappedAccountAsync(
                            cn.CompanyId,
                            SystemTransactionType.CreditNote,
                            isDebit: true,
                            defaultAccountId: line.Item.SalesIncomeAccountId);
                    }

                    if (revenueAccount == Guid.Empty)
                        return $"Posting Aborted: Item '{line.Item.Name}' is missing a valid Sales Income GL Account mapping.";

                    decimal lineGrossTotalForeign = line.Quantity * line.UnitPrice;
                    decimal lineGrossTotalBase = Math.Round(lineGrossTotalForeign * rate, 2);

                    // A. Debit Revenue Reversal Account
                    glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = revenueAccount, Debit = lineGrossTotalBase, Credit = 0, Reference = $"Credit Note : {line.Item.Name}" });

                    // B. Credit Discount Reversal Account
                    decimal lineDiscountForeign = 0;
                    if (so.DiscountPercentage > 0)
                    {
                        lineDiscountForeign = lineGrossTotalForeign * (so.DiscountPercentage / 100);
                    }
                    else if (so.DiscountAmount > 0 && originalSubTotalForeign > 0)
                    {
                        decimal allocationProportion = lineGrossTotalForeign / originalSubTotalForeign;
                        lineDiscountForeign = allocationProportion * so.DiscountAmount;
                    }

                    decimal lineDiscountBase = Math.Round(lineDiscountForeign * rate, 2);
                    if (lineDiscountBase > 0)
                    {
                        glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = discountAccount, Debit = 0, Credit = lineDiscountBase, Reference = $"Discount Rollback: {line.Item.Code}" });
                    }

                    // C. Debit Tax Reversal Account
                    decimal lineNetRevenueForeign = lineGrossTotalForeign - lineDiscountForeign;
                    decimal lineNetRevenueBase = lineGrossTotalBase - lineDiscountBase;

                    decimal lineTaxForeign = 0;
                    decimal lineTaxBase = 0;
                    if (taxPer > 0)
                    {
                        lineTaxForeign = lineNetRevenueForeign * (taxPer / 100);
                        lineTaxBase = Math.Round(lineNetRevenueBase * (taxPer / 100), 2);
                        glLines.Add(new GLJournalLine { DimensionSetId = line.DimensionSetId, IsDimensionDetail = true,  SegCoaId = taxGlAccountId, Debit = lineTaxBase, Credit = 0, Reference = $"Tax Rollback: {line.Item.Code}" });
                    }

                    totalArReductionBase += (lineNetRevenueBase + lineTaxBase);
                    totalArReductionForeign += (lineNetRevenueForeign + lineTaxForeign);
                }

                if (!glLines.Any()) return "No valid item line corrections were submitted.";

                // D. Credit Accounts Receivable balancing element
                var arLine = new GLJournalLine { SegCoaId = arAccount, Debit = 0, Credit = totalArReductionBase, Reference = $"AR Adjust: {so.OrderNumber}" };
                glLines.Add(arLine);

                // Self-balancing tolerance check
                decimal totalDebits = glLines.Sum(l => l.Debit);
                decimal totalCredits = glLines.Sum(l => l.Credit);
                decimal mismatch = totalDebits - totalCredits;

                if (Math.Abs(mismatch) > 0 && Math.Abs(mismatch) <= 0.10m)
                {
                    arLine.Credit += mismatch;
                }
                else if (Math.Abs(mismatch) > 0.10m)
                {
                    return $"Posting Aborted: Structural variance too wide to resolve safely. Mismatch: {mismatch:N2}";
                }

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "Credit Note", cn.CreditNoteNumber, glLines, userId.ToString(), existingBatchId: cn.GlBatchId, inheritedProjectId: cn.ProjectId ?? so.ProjectId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(cn.CompanyId, batchId.Value, userId.ToString());
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception(postErr);
                }

                cn.TotalAmount = Math.Round(totalArReductionForeign, 2);
                cn.Status = CreditNoteStatus.Posted;
                cn.PostedAt = DateTime.UtcNow;
                cn.PostedByUserId = userId;
                cn.GlBatchId = batchId;

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Financial Posting Mismatch Error: {ex.Message}";
            }
        }

        public Task<string> PostFinancialRefundDraftAsync(Guid cnId, Guid targetBankGlId, Guid userId) => Task.FromResult("Credit notes reverse invoice quantities. Use Receipt Refund for customer cash refunds.");

        public async Task<string> SaveDraftAsync(CreditNote note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.CreditNotes
                .Include(c => c.Lines)
                .Include(c => c.SalesOrder).ThenInclude(so => so.Lines)
                .FirstOrDefaultAsync(c => c.Id == note.Id);

            if (existing == null) return "Credit Note tracking entity not found.";
            if (!existing.ReturnToStock) return "Credit notes reverse invoice quantities. Use Receipt Refund for customer cash refunds.";
            if (existing.SalesInvoiceId.HasValue) return await new NewInvoiceCorrectionService(_dbFactory, _glOps, _numberingService).SaveCreditAsync(note);
            var existingBatch = existing.GlBatchId.HasValue
                ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == existing.GlBatchId.Value && b.CompanyId == existing.CompanyId)
                : null;
            if (existing.Status == CreditNoteStatus.Posted && (existingBatch == null || existingBatch.Status == BatchStatus.Posted))
                return "Cannot edit locked records.";
            if (existingBatch?.Status == BatchStatus.Ready) return "Cannot edit a credit note while it is awaiting General Ledger review.";

            if (existing.Status == CreditNoteStatus.Void) return "A void note cannot be edited.";
            if (existing.SalesOrder == null) return "The source invoice is unavailable.";
            if (note.Lines.Count != existing.Lines.Count || note.Lines.Select(x => x.Id).Distinct().Count() != note.Lines.Count)
                return "Invoice items cannot be added or removed from this note.";
            var prior = await ctx.CreditNoteLines.Where(x => x.HeaderId != existing.Id && x.Header!.SalesOrderId == existing.SalesOrderId && x.Header.ReturnToStock && x.Header.Status != CreditNoteStatus.Void)
                .Where(x => x.SalesOrderLineId.HasValue).GroupBy(x => x.SalesOrderLineId!.Value)
                .ToDictionaryAsync(x => x.Key, x => x.Sum(y => y.Quantity));
            foreach (var input in note.Lines)
            {
                var saved = existing.Lines.SingleOrDefault(x => x.Id == input.Id);
                if (saved == null) return "The invoice items have changed. Reload this note.";
                var source = existing.SalesOrder.Lines.SingleOrDefault(x => x.Id == saved!.SalesOrderLineId);
                if (saved == null || source == null || input.SalesOrderLineId != saved.SalesOrderLineId || input.ItemId != source.ItemId
                    || input.UnitPrice != source.UnitPrice || input.UomId != source.UomId || input.UomConversionFactor != source.UomConversionFactor)
                    return "Only the reversal quantity can be changed. Invoice items, prices and units stay fixed.";
                if (input.Quantity < 0 || input.Quantity > Math.Max(0, source.Quantity - prior.GetValueOrDefault(source.Id)))
                    return "The reversal quantity exceeds the available invoice quantity.";
            }

            existing.Date = note.Date;
            existing.TransactionDateTime = note.TransactionDateTime;
            existing.Reason = note.Reason;
            existing.CustomTransactionTypeId = note.CustomTransactionTypeId;
            existing.OverrideRevenueGlAccountId = note.OverrideRevenueGlAccountId;
            existing.OverrideReceivablesGlAccountId = note.OverrideReceivablesGlAccountId;


            decimal totalNetCreditForeign = 0;
            decimal originalSubTotalForeign = existing.SalesOrder?.Lines.Sum(l => l.Quantity * l.UnitPrice) ?? 0;

            decimal taxPer = 0;
            if (existing.SalesOrder?.TaxId != null)
            {
                var tax = await ctx.Taxes.FindAsync(existing.SalesOrder.TaxId.Value);
                if (tax != null) taxPer = tax.Per;
            }

            foreach (var line in note.Lines)
            {
                existing.Lines.Single(x => x.Id == line.Id).Quantity = line.Quantity;

                decimal lineGrossForeign = line.Quantity * line.UnitPrice;
                decimal lineDiscountForeign = 0;

                if (existing.SalesOrder?.DiscountPercentage > 0)
                {
                    lineDiscountForeign = lineGrossForeign * (existing.SalesOrder.DiscountPercentage / 100);
                }
                else if (existing.SalesOrder?.DiscountAmount > 0 && originalSubTotalForeign > 0)
                {
                    lineDiscountForeign = (lineGrossForeign / originalSubTotalForeign) * existing.SalesOrder.DiscountAmount;
                }

                decimal lineNetForeign = lineGrossForeign - lineDiscountForeign;
                decimal lineTaxForeign = lineNetForeign * (taxPer / 100);

                totalNetCreditForeign += (lineNetForeign + lineTaxForeign);
            }

            existing.TotalAmount = Math.Round(totalNetCreditForeign, 2);
            existing.WarehouseId = null;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteDraftAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var cn = await ctx.CreditNotes.FindAsync(id);
            if (cn == null || cn.Status != CreditNoteStatus.Draft) return "Cannot drop entry paths.";
            ctx.CreditNotes.Remove(cn);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public Task<CreditNote> CreateFinancialRefundDraftAsync(Guid orderId, Guid userId) => throw new InvalidOperationException("Credit notes reverse invoice quantities. Use Receipt Refund for customer cash refunds.");
    }
}
