    using Microsoft.EntityFrameworkCore;
    using Primafit_ERP.Components.Models;
    using Primafit_ERP.Components.Models.Reporting;
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

            public CreditNoteService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps, InventoryService invService, TransactionMappingService mappingService)
            {
                _dbFactory = dbFactory;
                _glOps = glOps;
                _invService = invService;
                _mappingService = mappingService;
            }

        // =========================================================================
        // 1. CASCADING TRANSACTION LOOKUP WORKFLOWS (Customer-Isolated)
        // =========================================================================

        /// <summary>
        /// Loads only posted invoices for a specific customer that have physical shipments processed.
        /// Used strictly by the Stock-Related Credit Note view.
        /// </summary>
        /// 
        public async Task<List<SalesOrder>> GetInvoicesEligibleForAdjustmentAsync(Guid companyId, Guid customerId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Target invoices that are financially posted but pending final pre-shipment/pre-payment adjustments
            var validStatuses = new[] { OrderStatus.Invoiced, OrderStatus.PartiallyInvoiced };

            // 1. Fetch candidate invoices containing physical goods
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

            // 2. Aggregate all historical non-voided credit note line allocations for these invoices
            var historicalCreditsMap = await ctx.CreditNoteLines
                .Include(cnl => cnl.Header)
                .Where(cnl => invoiceIds.Contains(cnl.Header!.SalesOrderId)
                           && cnl.Header.Status != CreditNoteStatus.Void)
                .GroupBy(cnl => cnl.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            // 3. Evaluate if any line item still has open quantity left to adjust
            var eligibleInvoices = new List<SalesOrder>();
            foreach (var inv in invoices)
            {
                bool hasAdjustableQuantities = false;

                foreach (var line in inv.Lines)
                {
                    if (line.Item == null || line.Item.IsService) continue;

                    decimal alreadyCredited = historicalCreditsMap.TryGetValue(line.Id, out var creditedQty)
                        ? creditedQty
                        : 0;

                    // Target baseline invoice line quantity instead of fulfillment shipment records
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

        /// <summary>
        /// Loads only posted invoices for a specific customer where cash payments have been received.
        /// Used strictly by the Payment Reversal Bank Refund view.
        /// </summary>
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

                // Aggregate total payments historically applied against these invoices
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
                    // Dynamic line calculations
                    decimal subTotal = inv.Lines.Sum(l => l.Quantity * l.UnitPrice);
                    decimal discount = inv.DiscountPercentage > 0 ? subTotal * (inv.DiscountPercentage / 100) : inv.DiscountAmount;
                    inv.GrandTotalForeign = subTotal - discount; // Simple baseline fallback; adds tax locally if needed

                    decimal totalPaid = paymentsMap.ContainsKey(inv.Id) ? paymentsMap[inv.Id] : 0;
                    decimal totalRefunded = historicalRefundsMap.ContainsKey(inv.Id) ? historicalRefundsMap[inv.Id] : 0;

                    decimal remainingRefundableBalance = totalPaid - totalRefunded;

                    // Only make the invoice selectable if there is actually cash left to reverse
                    if (remainingRefundableBalance > 0.01m)
                    {
                        inv.AmountPaid = remainingRefundableBalance; // Stored temporarily for display on the front-end card
                        eligibleList.Add(inv);
                    }
                }

                return eligibleList;
            }

        // =========================================================================
        // 2. FLOW A: PHYSICAL STOCK RETURNS ENGINE (Fulfillment Bound)
        // =========================================================================

        public async Task<CreditNote> CreateStockReturnDraftAsync(Guid orderId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var so = await ctx.SalesOrders
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == orderId);

            if (so == null) throw new Exception("Target sales invoice reference missing.");

            // Extract previously adjusted lines items across all existing historical non-voided credit notes
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
                ReturnToStock = true, // Retained to run through the line item calculation pipeline
                WarehouseId = so.WarehouseId,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                CreditNoteNumber = $"CNS-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}"
            };

            foreach (var soLine in so.Lines)
            {
                if (soLine.Item != null && soLine.Item.IsService) continue;

                decimal alreadyCredited = previousReturns.TryGetValue(soLine.Id, out var creditedQty) ? creditedQty : 0;

                // FIXED: Boundary rule is now tethered strictly to the Invoice Document Quantity
                decimal maxAdjustable = soLine.Quantity - alreadyCredited;

                if (maxAdjustable > 0)
                {
                    creditNote.Lines.Add(new CreditNoteLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = creditNote.Id,
                        ItemId = soLine.ItemId ?? Guid.Empty,
                        SalesOrderLineId = soLine.Id,
                        Quantity = 0, // Left blank for manual user input on the frontend grid matrix
                        UnitPrice = soLine.UnitPrice,
                        OriginalSoldQty = soLine.Quantity, // Repurposed to represent Original Invoice Quantity
                        MaxReturnableQty = maxAdjustable   // Repurposed to represent Max Adjustable Capacity
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
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);

            if (cn != null && cn.SalesOrder != null)
            {
                // Aggregate all OTHER posted/draft adjustments against this invoice, excluding the current document space
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
                        // FIXED: Forcing live hydration from invoice definition baseline variables
                        line.OriginalSoldQty = matchingInvoiceLine.Quantity;

                        decimal previouslyCredited = historicalReturnsMap.TryGetValue(line.SalesOrderLineId, out var creditedQty)
                            ? creditedQty
                            : 0;

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
                if (cn.Status == CreditNoteStatus.Posted) return "Document already locked.";
                if (cn.SalesOrder == null) return "Parent invoice reference missing from transaction context.";

                var so = cn.SalesOrder;

                // =========================================================================
                // 1. DYNAMIC INVOICE ACCOUNT RESOLUTION
                // =========================================================================
                bool hasDiscounts = so.DiscountPercentage > 0 || so.DiscountAmount > 0;
                Guid discountAccount = Guid.Empty;

                if (hasDiscounts)
                {
                    // FIXED: Prioritize the exact GL account specified on the source sales invoice header
                    discountAccount = so.DiscountGlAccountId ?? Guid.Empty;

                    // Fallback: If the invoice header didn't hard-code the ID, find it using the exact 
                    // same lookup rules the invoice engine used (isDebit: true) to avoid configuration errors
                    if (discountAccount == Guid.Empty)
                    {
                        discountAccount = await _mappingService.GetMappedAccountAsync(
                            cn.CompanyId,
                            SystemTransactionType.DiscountAllowed,
                            isDebit: true,
                            defaultAccountId: Guid.Empty);
                    }

                    if (discountAccount == Guid.Empty)
                        return "Posting Aborted: A discount is present on the invoice, but no valid Discount GL Account could be resolved from the source document or system configuration.";
                }

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
                            return $"Posting Aborted: Tax calculation rules apply ({taxDef.TaxCode}), but the Tax GL Account mapping is missing.";
                    }
                }

                Guid arAccount = await _mappingService.GetMappedAccountAsync(cn.CompanyId, SystemTransactionType.CreditNote, false, cn.Customer.ReceivablesAccountId.Value);
                if (arAccount == Guid.Empty)
                    return "Posting Aborted: Customer Accounts Receivable (AR) GL account mapping is unassigned.";

                // =========================================================================
                // 2. BALANCED JOURNAL ENTRY GENERATION
                // =========================================================================
                var glLines = new List<GLJournalLine>();
                decimal totalArReductionBase = 0;
                decimal totalArReductionForeign = 0; // ADDED: Track the net foreign currency amount for the header
                decimal originalSubTotalForeign = so.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal rate = cn.ExchangeRate > 0 ? cn.ExchangeRate : 1;

                foreach (var line in cn.Lines)
                {
                    if (line.Quantity <= 0) continue;
                    if (line.Item == null) continue;

                    Guid revenueAccount = await _mappingService.GetMappedAccountAsync(cn.CompanyId, SystemTransactionType.CreditNote, true, line.Item.SalesIncomeAccountId);
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

                    // Accumulate base for the GL row, and foreign for the document header
                    totalArReductionBase += (lineNetRevenueBase + lineTaxBase);
                    totalArReductionForeign += (lineNetRevenueForeign + lineTaxForeign); // FIXED: Capture true net line change
                }

                if (!glLines.Any()) return "No valid item line corrections were submitted.";

                // D. Create the baseline Accounts Receivable balancing element
                var arLine = new GLJournalLine { SegCoaId = arAccount, Debit = 0, Credit = totalArReductionBase, Reference = $"AR Adjust: {so.OrderNumber}" };
                glLines.Add(arLine);

                // =========================================================================
                // 3. LIVE JOURNAL SELF-BALANCING RECONCILIATION
                // =========================================================================
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

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "Credit Note", cn.CreditNoteNumber, glLines, userId.ToString());
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(cn.CompanyId, batchId.Value, userId.ToString());
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception(postErr);
                }

                // FIXED: Assign the true net adjusted credit value to the header property
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

        // =========================================================================
        // 3. FLOW B: FINANCIAL PAYMENT REVERSAL REFUNDS ENGINE (Direct Cash Outflow)
        // =========================================================================

        /// <summary>
        /// Executes a direct cash payment refund reversal loop, deducting money from bank assets and balancing client claims.
        /// </summary>
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

                    // 1. Debit Accounts Receivable (Re-opens invoice allocation room)
                    Guid arAccount = await _mappingService.GetMappedAccountAsync(cn.CompanyId, SystemTransactionType.CreditNote, true, cn.Customer.ReceivablesAccountId.Value);
                    glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = refundAmountBase, Credit = 0, Reference = $"Refund Claim: {cn.SalesOrder?.OrderNumber}" });

                    // 2. Credit Bank Account (Deducts money directly from banking assets)
                    glLines.Add(new GLJournalLine { SegCoaId = targetBankGlId, Debit = 0, Credit = refundAmountBase, Reference = $"Cash Refund Out: {cn.Customer.Name}" });

                    // Post to General Ledger Operations Service
                    var (err, batchId) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "Cash Refund Reversal", cn.CreditNoteNumber, glLines, userId.ToString());
                    if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                    if (batchId.HasValue) await _glOps.PostBatchAsync(cn.CompanyId, batchId.Value, userId.ToString());

                    // Update Draft Status to Posted
                    cn.Status = CreditNoteStatus.Posted;
                    cn.PostedAt = DateTime.UtcNow;
                    cn.PostedByUserId = userId;
                    cn.GlBatchId = batchId;

                    await ctx.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return string.Empty;
                }
                catch (Exception ex) { await transaction.RollbackAsync(); return ex.Message; }
            }

        // =========================================================================
        // 4. SHARED MANAGEMENT WORKFLOWS
        // =========================================================================
        public async Task<string> SaveDraftAsync(CreditNote note)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.CreditNotes
                .Include(c => c.Lines)
                .Include(c => c.SalesOrder).ThenInclude(so => so.Lines)
                .FirstOrDefaultAsync(c => c.Id == note.Id);

            if (existing == null) return "Credit Note tracking entity not found.";
            if (existing.Status == CreditNoteStatus.Posted) return "Cannot edit locked records.";

            existing.Date = note.Date;
            existing.Reason = note.Reason;

            // Remove old lines and sync new workspace values
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

                // Compute pro-rata values to determine true net draft totals
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

            // FIXED: Save the correct net adjusted total value to the draft header
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

                // Calculate previously posted and pending draft refunds to ensure accurate safety thresholds
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
                    ReturnToStock = false, // Purely financial cash reversal flag
                    TotalAmount = 0,       // Configured dynamically on the workspace input field
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