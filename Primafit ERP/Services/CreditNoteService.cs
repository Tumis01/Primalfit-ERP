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

        public CreditNoteService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            InventoryService invService,
            TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _invService = invService;
            _mappingService = mappingService;
        }

        public async Task<List<SalesOrder>> GetInvoicesEligibleForAdjustmentAsync(Guid companyId, Guid customerId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var validStatuses = new[] { OrderStatus.Invoiced, OrderStatus.PartiallyInvoiced };

            var invoices = await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId
                         && o.CustomerId == customerId
                         && o.OrderNumber.StartsWith("INV")
                         && validStatuses.Contains(o.Status)
                         && o.Lines.Any(l => l.Item != null && !l.Item.IsService))
                .ToListAsync();

            if (!invoices.Any()) return new List<SalesOrder>();

            var invoiceIds = invoices.Select(o => o.Id).ToList();

            var historicalCreditsMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => invoiceIds.Contains(cnl.Header!.SalesOrderId)
                           && cnl.Header.Status != CreditNoteStatus.Void)
                .GroupBy(cnl => cnl.SalesOrderLineId)
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

            return eligibleInvoices;
        }

        public async Task<List<SalesOrder>> GetPaidInvoicesByCustomerAsync(Guid companyId, Guid customerId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var validStatuses = new[] { OrderStatus.Invoiced, OrderStatus.PartiallyInvoiced, OrderStatus.Shipped, OrderStatus.PartiallyShipped };

            var invoices = await ctx.SalesOrders
                .Include(o => o.Currency)
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId
                         && o.CustomerId == customerId
                         && o.OrderNumber.StartsWith("INV")
                         && validStatuses.Contains(o.Status))
                .ToListAsync();

            var invoiceIds = invoices.Select(o => o.Id).ToList();

            var paymentsMap = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.AppliedAmount + x.CashDiscountTaken));

            var historicalRefundsMap = await ctx.CreditNotes
                .Where(cn => invoiceIds.Contains(cn.SalesOrderId)
                          && cn.Status == CreditNoteStatus.Posted
                          && cn.ReturnToStock == false)
                .GroupBy(cn => cn.SalesOrderId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.TotalAmount));

            var eligibleList = new List<SalesOrder>();
            foreach (var inv in invoices)
            {
                decimal subTotal = inv.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal discount = inv.DiscountPercentage > 0 ? subTotal * (inv.DiscountPercentage / 100) : inv.DiscountAmount;
                inv.GrandTotalForeign = subTotal - discount;

                decimal totalPaid = paymentsMap.GetValueOrDefault(inv.Id, 0);
                decimal totalRefunded = historicalRefundsMap.GetValueOrDefault(inv.Id, 0);

                decimal remainingRefundableBalance = totalPaid - totalRefunded;

                if (remainingRefundableBalance > 0.01m)
                {
                    inv.AmountPaid = remainingRefundableBalance;
                    eligibleList.Add(inv);
                }
            }

            return eligibleList;
        }

        public async Task<CreditNote> CreateStockReturnDraftAsync(Guid orderId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var so = await ctx.SalesOrders
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == orderId);

            if (so == null) throw new Exception("Target sales invoice reference missing.");

            var previousReturns = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => cnl.Header!.SalesOrderId == orderId && cnl.Header.Status != CreditNoteStatus.Void)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var creditNote = new CreditNote
            {
                Id = Guid.NewGuid(),
                CompanyId = so.CompanyId,
                SalesOrderId = so.Id,
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
                CreditNoteNumber = $"CNS-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}"
            };

            foreach (var soLine in so.Lines)
            {
                if (soLine.Item != null && soLine.Item.IsService) continue;

                decimal alreadyCredited = previousReturns.TryGetValue(soLine.Id, out var creditedQty) ? creditedQty : 0;
                decimal maxAdjustable = soLine.Quantity - alreadyCredited;

                if (maxAdjustable > 0)
                {
                    creditNote.Lines.Add(new CreditNoteLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = creditNote.Id,
                        ItemId = soLine.ItemId ?? Guid.Empty,
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
                .Include(c => c.Currency)
                .Include(c => c.Warehouse)
                .Include(c => c.CustomTransactionType)
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);

            if (cn != null && cn.SalesOrder != null)
            {
                var historicalReturnsMap = await ctx.CreditNoteLines
                    .Include(cnl => cnl.Header)
                    .Where(cnl => cnl.Header!.SalesOrderId == cn.SalesOrderId
                               && cnl.Header.Id != cn.Id
                               && cnl.Header.Status != CreditNoteStatus.Void)
                    .GroupBy(cnl => cnl.SalesOrderLineId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                foreach (var line in cn.Lines)
                {
                    var matchingInvoiceLine = cn.SalesOrder.Lines.FirstOrDefault(sol => sol.Id == line.SalesOrderLineId);
                    if (matchingInvoiceLine != null)
                    {
                        line.OriginalSoldQty = matchingInvoiceLine.Quantity;
                        decimal previouslyCredited = historicalReturnsMap.TryGetValue(line.SalesOrderLineId, out var creditedQty) ? creditedQty : 0;
                        line.MaxReturnableQty = matchingInvoiceLine.Quantity - previouslyCredited;
                    }
                }
            }

            return cn;
        }

        public async Task<string> PostStockCreditNoteAsync(Guid cnId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();
            try
            {
                var cn = await ctx.CreditNotes
                    .Include(c => c.Lines).ThenInclude(l => l.Item)
                    .Include(c => c.Customer)
                    .Include(c => c.SalesOrder).ThenInclude(so => so.Lines)
                    .FirstOrDefaultAsync(c => c.Id == cnId);

                if (cn == null) return "Credit note execution layout parameters not found.";
                if (cn.Status == CreditNoteStatus.Posted)
                {
                    var existingBatch = cn.GlBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == cn.GlBatchId.Value && b.CompanyId == cn.CompanyId)
                        : null;
                    if (existingBatch?.Status == BatchStatus.Posted) return "Document already committed to the General Ledger.";
                    if (existingBatch == null) return "Document is already locked and has no review batch.";
                }
                if (cn.SalesOrder == null) return "Parent invoice reference missing from transaction context.";

                var so = cn.SalesOrder;

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
                    glLines.Add(new GLJournalLine { SegCoaId = revenueAccount, Debit = lineGrossTotalBase, Credit = 0, Reference = $"Credit Note : {line.Item.Name}" });

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
                        glLines.Add(new GLJournalLine { SegCoaId = discountAccount, Debit = 0, Credit = lineDiscountBase, Reference = $"Discount Rollback: {line.Item.SKU}" });
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
                        glLines.Add(new GLJournalLine { SegCoaId = taxGlAccountId, Debit = lineTaxBase, Credit = 0, Reference = $"Tax Rollback: {line.Item.SKU}" });
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

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "Credit Note", cn.CreditNoteNumber, glLines, userId.ToString(), existingBatchId: cn.GlBatchId);
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

        public async Task<string> PostFinancialRefundDraftAsync(Guid cnId, Guid targetBankGlId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();
            try
            {
                var cn = await ctx.CreditNotes
                    .Include(c => c.Customer)
                    .Include(c => c.SalesOrder)
                    .FirstOrDefaultAsync(c => c.Id == cnId);

                if (cn == null) return "Refund tracking record not found.";
                if (cn.Status == CreditNoteStatus.Posted) return "Document is already posted.";
                if (cn.TotalAmount <= 0) return "Refund value must be greater than zero.";

                decimal currentRate = cn.ExchangeRate > 0 ? cn.ExchangeRate : 1;
                decimal refundAmountBase = Math.Round(cn.TotalAmount * currentRate, 2);

                var glLines = new List<GLJournalLine>();

                // Debit AR (Re-opens invoice allocation room)
                Guid arAccount = cn.OverrideReceivablesGlAccountId ?? Guid.Empty;
                if (arAccount == Guid.Empty)
                {
                    Guid defaultAr = cn.Customer?.ReceivablesAccountId ?? Guid.Empty;
                    arAccount = await _mappingService.GetMappedAccountAsync(
                        cn.CompanyId,
                        SystemTransactionType.CreditNote,
                        isDebit: true,
                        defaultAccountId: defaultAr);
                }

                glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = refundAmountBase, Credit = 0, Reference = $"Refund Claim: {cn.SalesOrder?.OrderNumber}" });

                // Credit Bank Account
                glLines.Add(new GLJournalLine { SegCoaId = targetBankGlId, Debit = 0, Credit = refundAmountBase, Reference = $"Cash Refund Out: {cn.Customer?.Name}" });

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "Cash Refund Reversal", cn.CreditNoteNumber, glLines, userId.ToString(), existingBatchId: cn.GlBatchId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(cn.CompanyId, batchId.Value, userId.ToString());

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
                return ex.Message;
            }
        }

        public async Task<string> SaveDraftAsync(CreditNote note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.CreditNotes
                .Include(c => c.Lines)
                .Include(c => c.SalesOrder).ThenInclude(so => so.Lines)
                .FirstOrDefaultAsync(c => c.Id == note.Id);

            if (existing == null) return "Credit Note tracking entity not found.";
            var existingBatch = existing.GlBatchId.HasValue
                ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == existing.GlBatchId.Value && b.CompanyId == existing.CompanyId)
                : null;
            if (existing.Status == CreditNoteStatus.Posted && (existingBatch == null || existingBatch.Status == BatchStatus.Posted))
                return "Cannot edit locked records.";

            existing.Date = note.Date;
            existing.Reason = note.Reason;
            existing.CustomTransactionTypeId = note.CustomTransactionTypeId;
            existing.OverrideRevenueGlAccountId = note.OverrideRevenueGlAccountId;
            existing.OverrideReceivablesGlAccountId = note.OverrideReceivablesGlAccountId;

            ctx.CreditNoteLines.RemoveRange(existing.Lines);

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
                ctx.CreditNoteLines.Add(new CreditNoteLine
                {
                    Id = Guid.NewGuid(),
                    HeaderId = existing.Id,
                    ItemId = line.ItemId,
                    SalesOrderLineId = line.SalesOrderLineId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    OriginalSoldQty = line.OriginalSoldQty,
                    MaxReturnableQty = line.MaxReturnableQty
                });

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

        public async Task<CreditNote> CreateFinancialRefundDraftAsync(Guid orderId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var so = await ctx.SalesOrders
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == orderId);

            if (so == null) throw new Exception("Target sales invoice reference missing.");

            decimal totalPaid = await ctx.PaymentApplications
                .Where(pa => pa.InvoiceId == orderId)
                .SumAsync(pa => pa.AppliedAmount + pa.CashDiscountTaken);

            decimal totalRefunded = await ctx.CreditNotes
                .Where(cn => cn.SalesOrderId == orderId && cn.Status != CreditNoteStatus.Void && cn.ReturnToStock == false)
                .SumAsync(cn => cn.TotalAmount);

            decimal maxRefundable = totalPaid - totalRefunded;
            if (maxRefundable <= 0.01m) throw new Exception("This invoice has already been fully refunded.");

            var creditNote = new CreditNote
            {
                Id = Guid.NewGuid(),
                CompanyId = so.CompanyId,
                SalesOrderId = so.Id,
                CustomerId = so.CustomerId,
                CurrencyId = so.CurrencyId,
                ExchangeRate = so.ExchangeRate,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = CreditNoteStatus.Draft,
                Reason = "Customer Payment Reversal Refund",
                ReturnToStock = false,
                TotalAmount = 0,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                CreditNoteNumber = $"CNF-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}"
            };

            ctx.CreditNotes.Add(creditNote);
            await ctx.SaveChangesAsync();
            return creditNote;
        }
    }
}
