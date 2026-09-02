using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class DebitNoteService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly TransactionMappingService _mappingService;

        public DebitNoteService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _mappingService = mappingService;
        }

        // =========================================================================
        // 1. DATA LOOKUP FILTERS (Targets Direct Invoice IDs)
        // =========================================================================

        public async Task<List<PurchaseOrder>> GetInvoicesEligibleForAdjustmentAsync(Guid companyId, Guid vendorId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var validStatuses = new[] { PurchaseOrderStatus.Invoiced, PurchaseOrderStatus.DraftInvoice };

            // Invoices in the PO engine are records with OrderNumber starting with "INV"
            var invoices = await ctx.PurchaseOrders
                .Include(o => o.Lines)
                .Include(o => o.Currency)
                .Where(o => o.CompanyId == companyId
                         && o.VendorId == vendorId
                         && o.OrderNumber.StartsWith("INV")
                         && (o.IsInvoicePosted || validStatuses.Contains(o.Status)))
                .ToListAsync();

            if (!invoices.Any()) return new List<PurchaseOrder>();

            var invoiceIds = invoices.Select(o => o.Id).ToList();

            // Sum only POSTED debit notes for the invoice
            var historicalDebitsMap = await ctx.DebitNoteLines
                .Include(dnl => dnl.Header)
                .Where(dnl => invoiceIds.Contains(dnl.Header!.PurchaseOrderId ?? Guid.Empty)
                           && dnl.Header.Status == DebitNoteStatus.Posted)
                .GroupBy(dnl => dnl.PurchaseOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var eligible = new List<PurchaseOrder>();
            foreach (var inv in invoices)
            {
                bool hasAdjustable = false;
                foreach (var line in inv.Lines)
                {
                    decimal alreadyDebited = historicalDebitsMap.TryGetValue(line.Id, out var qty) ? qty : 0;
                    if (line.QuantityOrdered - alreadyDebited > 0.001m)
                    {
                        hasAdjustable = true;
                        break;
                    }
                }
                if (hasAdjustable) eligible.Add(inv);
            }

            return eligible.OrderByDescending(o => o.OrderDate).ToList();
        }

        // =========================================================================
        // 2. INVOICE LINE CORRECTION WORKSPACE INITIALIZATION & RETRIEVAL
        // =========================================================================

        public async Task<DebitNote> CreateStockAdjustmentDraftAsync(Guid invoiceOrderId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var inv = await ctx.PurchaseOrders
                .Include(s => s.Lines)
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == invoiceOrderId);

            if (inv == null) throw new Exception("Target purchase invoice reference missing from database context.");

            // Checked strictly against the invoice record's lines
            var previousDebits = await ctx.DebitNoteLines
                .Include(dnl => dnl.Header)
                .Where(dnl => dnl.Header!.PurchaseOrderId == invoiceOrderId && dnl.Header.Status == DebitNoteStatus.Posted)
                .GroupBy(dnl => dnl.PurchaseOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var dn = new DebitNote
            {
                Id = Guid.NewGuid(),
                CompanyId = inv.CompanyId,
                PurchaseOrderId = inv.Id, // Stores the Invoice ID directly
                VendorId = inv.VendorId,
                CurrencyId = inv.CurrencyId,
                ExchangeRate = inv.ExchangeRate,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = DebitNoteStatus.Draft,
                Reason = "Purchase Invoice Line Item Adjustment",
                ReturnToStock = true,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                DebitNoteNumber = $"DNS-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}"
            };

            foreach (var line in inv.Lines)
            {
                decimal alreadyDebited = previousDebits.TryGetValue(line.Id, out var q) ? q : 0;
                decimal maxAdjustable = Math.Max(0, line.QuantityOrdered - alreadyDebited);

                if (maxAdjustable > 0.001m)
                {
                    dn.Lines.Add(new DebitNoteLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = dn.Id,
                        PurchaseOrderLineId = line.Id,
                        ItemId = line.ItemId,
                        Quantity = 0,
                        UnitCost = line.UnitCost,
                        OriginalPurchasedQty = line.QuantityOrdered,
                        MaxReturnableQty = maxAdjustable
                    });
                }
            }

            if (!dn.Lines.Any()) throw new Exception("This purchase invoice has already been completely cleared by previous debit notes.");

            ctx.DebitNotes.Add(dn);
            await ctx.SaveChangesAsync();
            return dn;
        }

        public async Task<DebitNote?> GetByIdAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var dn = await ctx.DebitNotes
                .Include(d => d.Lines).ThenInclude(l => l.Item)
                .Include(d => d.Vendor)
                .Include(d => d.PurchaseOrder).ThenInclude(o => o.Lines)
                .Include(d => d.Currency)
                .Include(d => d.CustomTransactionType)
                .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

            if (dn != null && dn.PurchaseOrder != null)
            {
                var historicalDebitsMap = await ctx.DebitNoteLines
                    .Include(dnl => dnl.Header)
                    .Where(dnl => dnl.Header!.PurchaseOrderId == dn.PurchaseOrderId
                               && dnl.Header.Id != dn.Id
                               && dnl.Header.Status == DebitNoteStatus.Posted)
                    .GroupBy(dnl => dnl.PurchaseOrderLineId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                foreach (var line in dn.Lines)
                {
                    var match = dn.PurchaseOrder.Lines.FirstOrDefault(pol => pol.Id == line.PurchaseOrderLineId);
                    if (match != null)
                    {
                        line.OriginalPurchasedQty = match.QuantityOrdered;
                        decimal prevDebited = historicalDebitsMap.TryGetValue(line.PurchaseOrderLineId, out var q) ? q : 0;
                        line.MaxReturnableQty = Math.Max(0, match.QuantityOrdered - prevDebited);
                    }
                }
            }

            return dn;
        }

        // =========================================================================
        // 3. POSTING ENGINE (Saves state exclusively to DebitNote)
        // =========================================================================

        public async Task<string> PostStockDebitNoteAsync(Guid dnId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();
            try
            {
                var dn = await ctx.DebitNotes
                    .Include(d => d.Lines).ThenInclude(l => l.Item)
                    .Include(d => d.Vendor)
                    .Include(d => d.PurchaseOrder).ThenInclude(po => po.Lines)
                    .FirstOrDefaultAsync(d => d.Id == dnId);

                if (dn == null) return "Debit note parameters not found.";
                if (dn.Status == DebitNoteStatus.Posted) return "Document is already posted and locked.";
                if (dn.PurchaseOrder == null) return "Parent purchase invoice reference missing.";

                var inv = dn.PurchaseOrder;

                TransactionGlMapping? customMapping = null;
                if (dn.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == dn.CompanyId && m.CustomTransactionTypeId == dn.CustomTransactionTypeId.Value);
                }

                // AP Account (Debit Leg: Liability Reduction)
                var vendor = await ctx.Vendors.FindAsync(dn.VendorId);
                Guid defaultApAccount = vendor?.PayablesAccountId ?? Guid.Empty;

                Guid apAccount = dn.OverrideAccountsPayableGlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? await _mappingService.GetMappedAccountAsync(
                        dn.CompanyId,
                        SystemTransactionType.DebitNote,
                        isDebit: true,
                        defaultAccountId: defaultApAccount);

                if (apAccount == Guid.Empty)
                    return "Posting Aborted: Vendor Accounts Payable (AP) GL account mapping is unassigned.";

                // Clearing / Inventory Account (Credit Leg)
                var grns = await ctx.GoodsReceipts
                    .AsNoTracking()
                    .Where(g => g.PurchaseOrderId == inv.Id && g.CompanyId == dn.CompanyId)
                    .ToListAsync();

                Guid defaultClearing = grns.FirstOrDefault(g => g.InventoryGlAccountId != Guid.Empty)?.InventoryGlAccountId ?? Guid.Empty;

                Guid clearingAccount = dn.OverrideGrIrClearingGlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? (defaultClearing != Guid.Empty ? defaultClearing : Guid.Empty);

                if (clearingAccount == Guid.Empty)
                {
                    clearingAccount = await _mappingService.GetMappedAccountAsync(
                        dn.CompanyId,
                        SystemTransactionType.DebitNote,
                        isDebit: false,
                        defaultAccountId: Guid.Empty);
                }

                if (clearingAccount == Guid.Empty)
                {
                    clearingAccount = await _mappingService.GetMappedAccountAsync(
                        dn.CompanyId,
                        SystemTransactionType.GoodsReceipt,
                        isDebit: false,
                        defaultAccountId: Guid.Empty);
                }

                // Discount Rollback
                bool hasDiscounts = inv.DiscountPercentage > 0 || inv.DiscountAmount > 0;
                Guid discountAccount = Guid.Empty;
                if (hasDiscounts)
                {
                    discountAccount = inv.DiscountGlAccountId ?? Guid.Empty;
                    if (discountAccount == Guid.Empty)
                    {
                        discountAccount = await _mappingService.GetMappedAccountAsync(
                            dn.CompanyId,
                            SystemTransactionType.DiscountReceived,
                            isDebit: true,
                            defaultAccountId: Guid.Empty);
                    }

                    if (discountAccount == Guid.Empty)
                        return "Posting Aborted: Missing Discount Received GL Account mapping for rollback.";
                }

                // Tax Rollback
                decimal taxPer = 0;
                Guid taxGlAccountId = Guid.Empty;
                if (inv.TaxId.HasValue)
                {
                    var taxDef = await ctx.Taxes.FindAsync(inv.TaxId.Value);
                    if (taxDef != null && taxDef.Per > 0)
                    {
                        taxPer = taxDef.Per;
                        taxGlAccountId = inv.TaxGLAccountId ?? taxDef.GLAccountId ?? Guid.Empty;
                        if (taxGlAccountId == Guid.Empty)
                            return $"Posting Aborted: Tax calculation rules apply ({taxDef.TaxCode}), but Tax GL Account mapping is missing.";
                    }
                }

                var glLines = new List<GLJournalLine>();
                decimal totalApReductionBase = 0;
                decimal totalApReductionForeign = 0;
                decimal originalSubTotalForeign = inv.Lines.Sum(l => l.QuantityOrdered * l.UnitCost);
                decimal rate = dn.ExchangeRate > 0 ? dn.ExchangeRate : 1;

                foreach (var line in dn.Lines)
                {
                    if (line.Quantity <= 0 || line.Item == null) continue;

                    decimal lineGrossForeign = line.Quantity * line.UnitCost;
                    decimal lineGrossBase = Math.Round(lineGrossForeign * rate, 2);

                    Guid targetReversalAccount = clearingAccount != Guid.Empty
                        ? clearingAccount
                        : (line.Item.InventoryAssetAccountId != Guid.Empty ? line.Item.InventoryAssetAccountId : defaultApAccount);

                    // Credit: Clearing/Expense/Inventory
                    glLines.Add(new GLJournalLine
                    {
                        SegCoaId = targetReversalAccount,
                        Debit = 0,
                        Credit = lineGrossBase,
                        Reference = $"Debit Note Adj: {line.Item.Name}"
                    });

                    // Debit: Discount Rollback
                    decimal lineDiscountForeign = 0;
                    if (inv.DiscountPercentage > 0)
                    {
                        lineDiscountForeign = lineGrossForeign * (inv.DiscountPercentage / 100);
                    }
                    else if (inv.DiscountAmount > 0 && originalSubTotalForeign > 0)
                    {
                        lineDiscountForeign = (lineGrossForeign / originalSubTotalForeign) * inv.DiscountAmount;
                    }

                    decimal lineDiscountBase = Math.Round(lineDiscountForeign * rate, 2);
                    if (lineDiscountBase > 0)
                    {
                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = discountAccount,
                            Debit = lineDiscountBase,
                            Credit = 0,
                            Reference = $"Discount Rollback: {line.Item.SKU}"
                        });
                    }

                    // Credit: Tax Claim Rollback
                    decimal lineNetForeign = lineGrossForeign - lineDiscountForeign;
                    decimal lineNetBase = lineGrossBase - lineDiscountBase;

                    decimal lineTaxForeign = 0;
                    decimal lineTaxBase = 0;
                    if (taxPer > 0)
                    {
                        lineTaxForeign = lineNetForeign * (taxPer / 100);
                        lineTaxBase = Math.Round(lineNetBase * (taxPer / 100), 2);

                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = taxGlAccountId,
                            Debit = 0,
                            Credit = lineTaxBase,
                            Reference = $"Tax Rollback: {line.Item.SKU}"
                        });
                    }

                    totalApReductionBase += (lineNetBase + lineTaxBase);
                    totalApReductionForeign += (lineNetForeign + lineTaxForeign);
                }

                if (!glLines.Any()) return "No valid item line corrections submitted.";

                // Debit: Accounts Payable (Reduces invoice liability)
                var apLine = new GLJournalLine
                {
                    SegCoaId = apAccount,
                    Debit = totalApReductionBase,
                    Credit = 0,
                    Reference = $"AP Reduction: {inv.OrderNumber}"
                };
                glLines.Add(apLine);

                // Auto-balancing threshold check
                decimal totalDebits = glLines.Sum(l => l.Debit);
                decimal totalCredits = glLines.Sum(l => l.Credit);
                decimal mismatch = totalDebits - totalCredits;

                if (Math.Abs(mismatch) > 0 && Math.Abs(mismatch) <= 0.10m)
                {
                    apLine.Debit -= mismatch;
                }
                else if (Math.Abs(mismatch) > 0.10m)
                {
                    return $"Posting Aborted: Structural variance too wide ({mismatch:N2}).";
                }

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    dn.CompanyId,
                    dn.Date,
                    "Debit Note",
                    dn.DebitNoteNumber,
                    glLines,
                    userId.ToString());

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(dn.CompanyId, batchId.Value, userId.ToString());

                // Save status strictly on Debit Note
                dn.TotalAmount = Math.Round(totalApReductionForeign, 2);
                dn.Status = DebitNoteStatus.Posted;
                dn.PostedAt = DateTime.UtcNow;
                dn.PostedByUserId = userId;
                dn.GlBatchId = batchId;

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Financial Posting Error: {ex.Message}";
            }
        }

        // =========================================================================
        // 4. DRAFT MANAGEMENT
        // =========================================================================

        public async Task<string> SaveDraftAsync(DebitNote note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.DebitNotes
                .Include(d => d.Lines)
                .Include(d => d.PurchaseOrder).ThenInclude(po => po.Lines)
                .FirstOrDefaultAsync(d => d.Id == note.Id);

            if (existing == null) return "Debit note tracking record not found.";
            if (existing.Status == DebitNoteStatus.Posted) return "Cannot edit locked records.";

            existing.Date = note.Date;
            existing.Reason = note.Reason;
            existing.CustomTransactionTypeId = note.CustomTransactionTypeId;
            existing.OverrideAccountsPayableGlAccountId = note.OverrideAccountsPayableGlAccountId;
            existing.OverrideGrIrClearingGlAccountId = note.OverrideGrIrClearingGlAccountId;

            ctx.DebitNoteLines.RemoveRange(existing.Lines);

            decimal totalNetDebitForeign = 0;
            decimal originalSubTotalForeign = existing.PurchaseOrder?.Lines.Sum(l => l.QuantityOrdered * l.UnitCost) ?? 0;

            decimal taxPer = 0;
            if (existing.PurchaseOrder?.TaxId != null)
            {
                var tax = await ctx.Taxes.FindAsync(existing.PurchaseOrder.TaxId.Value);
                if (tax != null) taxPer = tax.Per;
            }

            foreach (var line in note.Lines)
            {
                ctx.DebitNoteLines.Add(new DebitNoteLine
                {
                    Id = Guid.NewGuid(),
                    HeaderId = existing.Id,
                    ItemId = line.ItemId,
                    PurchaseOrderLineId = line.PurchaseOrderLineId,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    OriginalPurchasedQty = line.OriginalPurchasedQty,
                    MaxReturnableQty = line.MaxReturnableQty
                });

                decimal lineGross = line.Quantity * line.UnitCost;
                decimal lineDisc = 0;
                if (existing.PurchaseOrder?.DiscountPercentage > 0)
                {
                    lineDisc = lineGross * (existing.PurchaseOrder.DiscountPercentage / 100);
                }
                else if (existing.PurchaseOrder?.DiscountAmount > 0 && originalSubTotalForeign > 0)
                {
                    lineDisc = (lineGross / originalSubTotalForeign) * existing.PurchaseOrder.DiscountAmount;
                }

                decimal lineNet = lineGross - lineDisc;
                decimal lineTax = lineNet * (taxPer / 100);
                totalNetDebitForeign += (lineNet + lineTax);
            }

            existing.TotalAmount = Math.Round(totalNetDebitForeign, 2);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteDraftAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var dn = await ctx.DebitNotes.FindAsync(id);
            if (dn == null || dn.Status != DebitNoteStatus.Draft) return "Cannot drop entry paths.";
            ctx.DebitNotes.Remove(dn);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}