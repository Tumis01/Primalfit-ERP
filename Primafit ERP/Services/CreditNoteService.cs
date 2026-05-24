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
        public async Task<List<SalesOrder>> GetShippedInvoicesByCustomerAsync(Guid companyId, Guid customerId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var validStatuses = new[] { OrderStatus.Invoiced, OrderStatus.PartiallyInvoiced, OrderStatus.Shipped, OrderStatus.PartiallyShipped };

            return await ctx.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId
                         && o.CustomerId == customerId
                         && o.OrderNumber.StartsWith("INV")
                         && validStatuses.Contains(o.Status)
                         // FIXED: Strict sub-query filters out service items and un-shipped lines completely
                         && o.Lines.Any(l => l.Item != null && !l.Item.IsService && l.QtyShipped > 0))
                .AsNoTracking()
                .ToListAsync();
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

            // Extract previously returned lines items to prevent double-returns
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
                Reason = "Inventory Items Return Movement",
                ReturnToStock = true, // Force inventory logic context paths
                WarehouseId = so.WarehouseId,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                CreditNoteNumber = $"CNS-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}"
            };

            foreach (var soLine in so.Lines)
            {
                if (soLine.Item != null && soLine.Item.IsService) continue;

                decimal alreadyReturned = previousReturns.ContainsKey(soLine.Id) ? previousReturns[soLine.Id] : 0;

                // CRITICAL BOUNDARY RULE: Maximum allowed return is bound to what was actually *Shipped*, not ordered
                decimal maxReturnable = soLine.QtyShipped - alreadyReturned;

                if (maxReturnable > 0)
                {
                    creditNote.Lines.Add(new CreditNoteLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = creditNote.Id,
                        ItemId = soLine.ItemId ?? Guid.Empty,
                        SalesOrderLineId = soLine.Id,
                        Quantity = 0, // Prompt configuration entry manually on front-end grid matrix
                        UnitPrice = soLine.UnitPrice,
                        OriginalSoldQty = soLine.QtyShipped,
                        MaxReturnableQty = maxReturnable
                    });
                }
            }

            if (!creditNote.Lines.Any()) throw new Exception("All physically shipped line quantities have already been returned.");

            ctx.CreditNotes.Add(creditNote);
            await ctx.SaveChangesAsync();
            return creditNote;
        }
        public async Task<CreditNote?> GetByIdAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch the Credit Note and eagerly include the source Sales Order lines
            var cn = await ctx.CreditNotes
                .Include(c => c.Lines).ThenInclude(l => l.Item)
                .Include(c => c.Customer)
                .Include(c => c.SalesOrder).ThenInclude(o => o.Lines) // CRITICAL: Eagerly load parent invoice lines
                .Include(c => c.Currency)
                .Include(c => c.Warehouse)
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);

            // 2. Dynamically stitch and calculate live return limits from the invoice source of truth
            if (cn != null && cn.SalesOrder != null)
            {
                // Aggregate all OTHER posted/draft returns against this invoice to calculate max return limits accurately
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
                        // Force hydration from the source invoice line
                        line.OriginalSoldQty = matchingInvoiceLine.QtyShipped;

                        decimal previouslyReturned = historicalReturnsMap.ContainsKey(line.SalesOrderLineId)
                            ? historicalReturnsMap[line.SalesOrderLineId]
                            : 0;

                        line.MaxReturnableQty = matchingInvoiceLine.QtyShipped - previouslyReturned;
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
                    .Include(c => c.SalesOrder)
                    .FirstOrDefaultAsync(c => c.Id == cnId);

                if (cn == null) return "Credit note execution layout parameters not found.";
                if (cn.Status == CreditNoteStatus.Posted) return "Document already locked.";
                if (cn.WarehouseId == null || cn.WarehouseId == Guid.Empty) return "Warehouse allocation target is required.";

                var glLines = new List<GLJournalLine>();
                var cogsGlLines = new List<GLJournalLine>();
                decimal totalRevenueReversalBase = 0;

                foreach (var line in cn.Lines)
                {
                    if (line.Quantity <= 0) continue;

                    // 1. Reverse Revenue Mappings (Debit)
                    Guid revenueAccount = await _mappingService.GetMappedAccountAsync(cn.CompanyId, SystemTransactionType.CreditNote, true, line.Item.SalesIncomeAccountId);
                    decimal lineTotalBase = Math.Round(line.LineTotal * cn.ExchangeRate, 2);

                    glLines.Add(new GLJournalLine { SegCoaId = revenueAccount, Debit = lineTotalBase, Credit = 0, Reference = $"Return Rev: {line.Item.Name}" });
                    totalRevenueReversalBase += lineTotalBase;

                    // 2. Physical Inventory Movement Insertion (Drives + Stock counts)
                    ctx.StockLedgers.Add(new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = cn.CompanyId,
                        ItemId = line.ItemId,
                        WarehouseId = cn.WarehouseId.Value,
                        QuantityChanged = line.Quantity,
                        Type = StockMovementType.SalesReturn,
                        CostAtTime = line.Item.WeightedAverageCost,
                        Reference = cn.CreditNoteNumber,
                        Date = DateTime.UtcNow
                    });

                    // 3. Asset Revaluation GL Pairing (Debit Inventory / Credit COGS)
                    decimal cogsValue = Math.Round(line.Quantity * line.Item.WeightedAverageCost, 2);
                    if (cogsValue > 0)
                    {
                        cogsGlLines.Add(new GLJournalLine { SegCoaId = line.Item.InventoryAssetAccountId, Debit = cogsValue, Credit = 0, Reference = $"Stock Return: {line.Item.SKU}" });
                        cogsGlLines.Add(new GLJournalLine { SegCoaId = line.Item.CostOfGoodsSoldAccountId, Debit = 0, Credit = cogsValue, Reference = $"COGS Return: {line.Item.SKU}" });
                    }
                }

                // 4. Reverse Accounts Receivable Asset Leg (Credit)
                Guid arAccount = await _mappingService.GetMappedAccountAsync(cn.CompanyId, SystemTransactionType.CreditNote, false, cn.Customer.ReceivablesAccountId.Value);
                glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = 0, Credit = totalRevenueReversalBase, Reference = $"AR Rev {cn.CreditNoteNumber}" });

                // Commit balanced accounting bundles
                var (err1, b1) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "Sales Return", cn.CreditNoteNumber, glLines, userId.ToString());
                if (!string.IsNullOrEmpty(err1)) throw new Exception(err1);
                if (b1.HasValue) await _glOps.PostBatchAsync(cn.CompanyId, b1.Value, userId.ToString());

                if (cogsGlLines.Any())
                {
                    var (err2, b2) = await _glOps.CreateJournalEntryAsync(cn.CompanyId, cn.Date, "Inventory Revaluation", cn.CreditNoteNumber, cogsGlLines, userId.ToString());
                    if (!string.IsNullOrEmpty(err2)) throw new Exception(err2);
                    if (b2.HasValue) await _glOps.PostBatchAsync(cn.CompanyId, b2.Value, userId.ToString());
                }

                cn.Status = CreditNoteStatus.Posted;
                cn.PostedAt = DateTime.UtcNow;
                cn.PostedByUserId = userId;

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex) { await transaction.RollbackAsync(); return ex.Message; }
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
            var existing = await ctx.CreditNotes.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == note.Id);
            if (existing == null) return "Credit Note tracking entity not found.";
            if (existing.Status == CreditNoteStatus.Posted) return "Cannot edit locked records.";

            existing.Date = note.Date;
            existing.Reason = note.Reason;

            // Safety check for multi-tenant boundary configurations
            if (existing.ReturnToStock)
            {
                existing.WarehouseId = note.WarehouseId;
                existing.TotalAmount = note.Lines.Sum(l => l.LineTotal); // Recalculate from item lines
            }
            else
            {
                existing.WarehouseId = null; // Enforce null to prevent foreign key database conflicts
                existing.TotalAmount = note.TotalAmount; // FIXED: Safely persist direct header user inputs
            }

            ctx.CreditNoteLines.RemoveRange(existing.Lines);
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
            }

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