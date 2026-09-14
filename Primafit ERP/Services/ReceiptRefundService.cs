using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class ReceiptRefundService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly TransactionMappingService _mappingService;

        public ReceiptRefundService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _mappingService = mappingService;
        }

        public async Task<List<SalesOrder>> GetInvoicesEligibleForRefundAsync(Guid companyId, Guid customerId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var validStatuses = new[] { OrderStatus.Invoiced, OrderStatus.PartiallyInvoiced, OrderStatus.Shipped, OrderStatus.PartiallyShipped };

            var invoices = await ctx.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Currency)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId
                         && o.CustomerId == customerId
                         && o.OrderNumber.StartsWith("INV")
                         && validStatuses.Contains(o.Status))
                .ToListAsync();

            if (!invoices.Any()) return new List<SalesOrder>();

            var invoiceIds = invoices.Select(o => o.Id).ToList();

            var paymentsMap = await (from pa in ctx.PaymentApplications
                                     join p in ctx.CustomerPayments on pa.CustomerPaymentId equals p.Id
                                     where invoiceIds.Contains(pa.InvoiceId) && p.Status == PaymentStatus.Posted
                                        && (!p.GLBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == p.GLBatchId.Value && b.Status == BatchStatus.Posted))
                                     group pa by pa.InvoiceId into g
                                     select new { InvoiceId = g.Key, Paid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                                    .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid);

            var historicalCashRefundsMap = await ctx.ReceiptRefunds
                .Where(r => invoiceIds.Contains(r.SalesOrderId)
                         && r.Status == ReceiptRefundStatus.Posted
                         && (!r.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == r.GlBatchId.Value && b.Status == BatchStatus.Posted))
                         && (r.RefundType == ReceiptRefundType.PaymentOnly || r.RefundType == ReceiptRefundType.Both))
                .GroupBy(r => r.SalesOrderId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.TotalAmount));

            var historicalQtyReturnsMap = await ctx.ReceiptRefundLines
                .Include(l => l.Header)
                .Where(l => invoiceIds.Contains(l.Header!.SalesOrderId) && l.Header.Status == ReceiptRefundStatus.Posted
                         && (!l.Header.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == l.Header.GlBatchId.Value && b.Status == BatchStatus.Posted)))
                .GroupBy(l => l.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var eligibleList = new List<SalesOrder>();

            foreach (var inv in invoices)
            {
                decimal totalPaid = paymentsMap.TryGetValue(inv.Id, out var paidAmt) ? paidAmt : 0;
                decimal totalCashRefunded = historicalCashRefundsMap.TryGetValue(inv.Id, out var refAmt) ? refAmt : 0;
                decimal remainingCashLimit = totalPaid - totalCashRefunded;

                bool hasReturnableStock = inv.Lines.Any(l =>
                {
                    if (l.Item == null || l.Item.IsService) return false;
                    decimal alreadyReturned = historicalQtyReturnsMap.TryGetValue(l.Id, out var ret) ? ret : 0;
                    return (l.QtyShipped - alreadyReturned) > 0.001m;
                });

                if (remainingCashLimit > 0.01m || hasReturnableStock)
                {
                    eligibleList.Add(inv);
                }
            }

            return eligibleList.OrderByDescending(o => o.Date).ToList();
        }

        public async Task<ReceiptRefund> CreateReceiptRefundDraftAsync(Guid orderId, ReceiptRefundType type, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var so = await ctx.SalesOrders
                .Include(s => s.Lines).ThenInclude(l => l.Item)
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == orderId);

            if (so == null) throw new Exception("Target invoice reference missing from database context.");

            var historicalQtyReturnsMap = await ctx.ReceiptRefundLines
                .Include(l => l.Header)
                .Where(l => l.Header!.SalesOrderId == orderId && l.Header.Status == ReceiptRefundStatus.Posted
                         && (!l.Header.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == l.Header.GlBatchId.Value && b.Status == BatchStatus.Posted)))
                .GroupBy(l => l.SalesOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var refund = new ReceiptRefund
            {
                Id = Guid.NewGuid(),
                CompanyId = so.CompanyId,
                SalesOrderId = so.Id,
                CustomerId = so.CustomerId,
                CurrencyId = so.CurrencyId,
                ExchangeRate = so.ExchangeRate,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = ReceiptRefundStatus.Draft,
                RefundType = type,
                Reason = "Customer Receipt Return Execution Workspace",
                DestinationWarehouseId = so.WarehouseId,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                RefundNumber = $"RRF-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}"
            };

            foreach (var soLine in so.Lines)
            {
                if (soLine.Item != null && soLine.Item.IsService) continue;

                decimal alreadyReturnedQty = historicalQtyReturnsMap.TryGetValue(soLine.Id, out var q) ? q : 0;
                decimal maxReturnableQty = Math.Max(0, soLine.QtyShipped - alreadyReturnedQty);

                if (type == ReceiptRefundType.PaymentOnly || maxReturnableQty > 0)
                {
                    refund.Lines.Add(new ReceiptRefundLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = refund.Id,
                        ItemId = soLine.ItemId ?? Guid.Empty,
                        UomId = soLine.UomId,
                        UomName = soLine.UomName,
                        UomConversionFactor = soLine.UomConversionFactor,
                        SalesOrderLineId = soLine.Id,
                        Quantity = 0,
                        UnitPrice = soLine.UnitPrice,
                        MaxAdjustableQty = maxReturnableQty,
                        OriginalShippedQty = soLine.QtyShipped,
                        PreviouslyReturnedQty = alreadyReturnedQty
                    });
                }
            }

            ctx.ReceiptRefunds.Add(refund);
            await ctx.SaveChangesAsync();
            return refund;
        }

        public async Task<ReceiptRefund?> GetByIdAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var refund = await ctx.ReceiptRefunds
                .Include(r => r.Lines).ThenInclude(l => l.Item)
                .Include(r => r.Customer)
                .Include(r => r.Currency)
                .Include(r => r.SalesOrder).ThenInclude(so => so.Lines)
                .Include(r => r.CustomTransactionType)
                .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

            if (refund != null && refund.SalesOrder != null)
            {
                var so = refund.SalesOrder;

                var historicalQtyReturnsMap = await ctx.ReceiptRefundLines
                    .Include(l => l.Header)
                    .Where(l => l.Header!.SalesOrderId == refund.SalesOrderId
                             && l.Header.Id != refund.Id
                         && l.Header.Status == ReceiptRefundStatus.Posted
                         && (!l.Header.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == l.Header.GlBatchId.Value && b.Status == BatchStatus.Posted)))
                    .GroupBy(l => l.SalesOrderLineId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                foreach (var line in refund.Lines)
                {
                    var matchingInvoiceLine = so.Lines.FirstOrDefault(sol => sol.Id == line.SalesOrderLineId);
                    if (matchingInvoiceLine != null)
                    {
                        decimal alreadyReturnedQty = historicalQtyReturnsMap.TryGetValue(line.SalesOrderLineId, out var q) ? q : 0;
                        line.MaxAdjustableQty = Math.Max(0, matchingInvoiceLine.QtyShipped - alreadyReturnedQty);
                        line.OriginalShippedQty = matchingInvoiceLine.QtyShipped;
                        line.PreviouslyReturnedQty = alreadyReturnedQty;
                    }
                }
            }

            return refund;
        }

        public async Task<string> SaveDraftAsync(ReceiptRefund refund)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.ReceiptRefunds.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == refund.Id);

            if (existing == null) return "Receipt return document path missing.";
            var existingBatch = existing.GlBatchId.HasValue
                ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == existing.GlBatchId.Value && b.CompanyId == existing.CompanyId)
                : null;
            if (existing.Status == ReceiptRefundStatus.Posted && (existingBatch == null || existingBatch.Status == BatchStatus.Posted))
                return "Cannot modify posted accounting entries.";

            existing.Date = refund.Date;
            existing.TransactionDateTime = refund.TransactionDateTime;
            existing.Reason = refund.Reason;
            existing.RefundType = refund.RefundType;
            existing.BankAccountId = refund.BankAccountId;
            existing.DestinationWarehouseId = refund.DestinationWarehouseId;
            existing.TotalAmount = refund.TotalAmount;
            existing.CustomTransactionTypeId = refund.CustomTransactionTypeId;
            existing.OverrideReceivablesGlAccountId = refund.OverrideReceivablesGlAccountId;
            existing.OverrideInventoryAssetGlAccountId = refund.OverrideInventoryAssetGlAccountId;
            existing.OverrideCogsGlAccountId = refund.OverrideCogsGlAccountId;

            ctx.ReceiptRefundLines.RemoveRange(existing.Lines);
            foreach (var line in refund.Lines)
            {
                ctx.ReceiptRefundLines.Add(new ReceiptRefundLine
                {
                    Id = Guid.NewGuid(),
                    HeaderId = existing.Id,
                    ItemId = line.ItemId,
                    UomId = line.UomId,
                    UomName = line.UomName,
                    UomConversionFactor = line.UomConversionFactor,
                    SalesOrderLineId = line.SalesOrderLineId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                });
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteDraftAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.ReceiptRefunds.FindAsync(id);

            if (existing == null || existing.Status != ReceiptRefundStatus.Draft)
                return "Invalid workspace purge configuration reference mapping.";

            ctx.ReceiptRefunds.Remove(existing);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> PostReceiptRefundAsync(Guid refundId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();
            try
            {
                var refund = await ctx.ReceiptRefunds
                    .Include(r => r.Lines).ThenInclude(l => l.Item)
                    .Include(r => r.Customer)
                    .Include(r => r.SalesOrder).ThenInclude(so => so.Lines)
                    .FirstOrDefaultAsync(r => r.Id == refundId);

                if (refund == null) return "Refund parameters not found.";
                if (refund.Status == ReceiptRefundStatus.Posted)
                {
                    var existingBatch = refund.GlBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == refund.GlBatchId.Value && b.CompanyId == refund.CompanyId)
                        : null;
                    if (existingBatch?.Status == BatchStatus.Posted) return "Document is already committed to the General Ledger.";
                    if (existingBatch == null) return "Document is already locked and has no review batch.";
                }
                if (refund.SalesOrder == null) return "Parent sales order reference mapping is missing.";

                var so = refund.SalesOrder;
                var glLines = new List<GLJournalLine>();
                decimal rate = refund.ExchangeRate > 0 ? refund.ExchangeRate : 1;

                TransactionGlMapping? customMapping = null;
                if (refund.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == refund.CompanyId && m.CustomTransactionTypeId == refund.CustomTransactionTypeId.Value);
                }

                // -----------------------------------------------------------------
                // LEG A: PHYSICAL STOCK RETURN (SystemTransactionType.ReceiptRefundStock)
                // DEBIT: Inventory Asset | CREDIT: Cost of Goods Sold (COGS)
                // -----------------------------------------------------------------
                if (refund.RefundType == ReceiptRefundType.QuantityOnly || refund.RefundType == ReceiptRefundType.Both)
                {
                    if (refund.DestinationWarehouseId == null || refund.DestinationWarehouseId == Guid.Empty)
                        return "Posting Aborted: A destination warehouse location is required to log product inventory returns.";

                    var historicalQtyReturnsMap = await ctx.ReceiptRefundLines
                        .Include(l => l.Header)
                        .Where(l => l.Header!.SalesOrderId == so.Id
                                 && l.Header.Id != refund.Id
                                 && l.Header.Status == ReceiptRefundStatus.Posted
                                 && (!l.Header.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == l.Header.GlBatchId.Value && b.Status == BatchStatus.Posted)))
                        .GroupBy(l => l.SalesOrderLineId)
                        .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                    foreach (var line in refund.Lines)
                    {
                        if (line.Quantity <= 0) continue;
                        if (line.Item == null) continue;

                        var soLine = so.Lines.FirstOrDefault(sl => sl.Id == line.SalesOrderLineId);
                        if (soLine == null) return $"Line mapping error for product reference {line.Item.Name}.";

                        decimal alreadyReturnedQty = historicalQtyReturnsMap.TryGetValue(line.SalesOrderLineId, out var q) ? q : 0;
                        decimal maxAllowedReturnQty = soLine.QtyShipped - alreadyReturnedQty;

                        if (line.Quantity > maxAllowedReturnQty + 0.001m)
                            return $"Posting Aborted: Item '{line.Item.Name}' quantity returned ({line.Quantity:N2}) exceeds remaining physical shipment allowance ({maxAllowedReturnQty:N2}).";

                        decimal resolvedUnitCost = line.Item.CostingType switch
                        {
                            CostingMethod.WACC => line.Item.WeightedAverageCost,
                            CostingMethod.StandardCosting => line.Item.StandardCost,
                            CostingMethod.UserSpecified => line.Item.UserSpecifiedCost,
                            CostingMethod.MostRecentCost => line.Item.MostRecentCost,
                            _ => line.Item.WeightedAverageCost
                        };

                        // 1. Log inventory intake in StockLedger
                        ctx.StockLedgers.Add(new StockLedger
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = refund.CompanyId,
                            ItemId = line.ItemId,
                            WarehouseId = refund.DestinationWarehouseId.Value,
                            QuantityChanged = line.Quantity,
                            Type = StockMovementType.SalesReturn,
                            CostAtTime = resolvedUnitCost,
                            Reference = refund.RefundNumber,
                            Date = DateTime.UtcNow
                        });

                        // 2. Double-entry routing: DR Inventory Asset | CR COGS
                        decimal lineCogsValueBase = Math.Round(line.Quantity * resolvedUnitCost, 2);
                        if (lineCogsValueBase > 0)
                        {
                            Guid inventoryAssetAccount = refund.OverrideInventoryAssetGlAccountId
                                ?? customMapping?.OverrideDebitGlAccountId
                                ?? await _mappingService.GetMappedAccountAsync(
                                    refund.CompanyId,
                                    SystemTransactionType.ReceiptRefundStock,
                                    isDebit: true,
                                    defaultAccountId: line.Item.InventoryAssetAccountId);

                            Guid cogsAccount = refund.OverrideCogsGlAccountId
                                ?? customMapping?.OverrideCreditGlAccountId
                                ?? await _mappingService.GetMappedAccountAsync(
                                    refund.CompanyId,
                                    SystemTransactionType.ReceiptRefundStock,
                                    isDebit: false,
                                    defaultAccountId: line.Item.CostOfGoodsSoldAccountId);

                            if (inventoryAssetAccount == Guid.Empty)
                                return $"Posting Aborted: Item '{line.Item.Name}' is missing an Inventory Asset GL Account mapping.";

                            if (cogsAccount == Guid.Empty)
                                return $"Posting Aborted: Item '{line.Item.Name}' is missing a Cost of Goods Sold (COGS) GL Account mapping.";

                            glLines.Add(new GLJournalLine { SegCoaId = inventoryAssetAccount, Debit = lineCogsValueBase, Credit = 0, Reference = $"Return Stock: {line.Item.SKU}" });
                            glLines.Add(new GLJournalLine { SegCoaId = cogsAccount, Debit = 0, Credit = lineCogsValueBase, Reference = $"Return COGS: {line.Item.SKU}" });
                        }
                    }
                }

                // -----------------------------------------------------------------
                // LEG B: PAYMENT CASH REFUND (SystemTransactionType.ReceiptRefundCash)
                // DEBIT: Customer AR | CREDIT: Disbursing Bank / Cash Account
                // -----------------------------------------------------------------
                if (refund.RefundType == ReceiptRefundType.PaymentOnly || refund.RefundType == ReceiptRefundType.Both)
                {
                    if (!refund.BankAccountId.HasValue || refund.BankAccountId.Value == Guid.Empty)
                        return "Posting Aborted: Bank account is required for cash refund disbursement.";

                    Guid defaultAr = refund.Customer?.ReceivablesAccountId ?? Guid.Empty;

                    Guid debitArAccount = refund.OverrideReceivablesGlAccountId
                        ?? customMapping?.OverrideDebitGlAccountId
                        ?? await _mappingService.GetMappedAccountAsync(
                            refund.CompanyId,
                            SystemTransactionType.ReceiptRefundCash,
                            isDebit: true,
                            defaultAccountId: defaultAr);

                    Guid creditDisbursingAccount = customMapping?.OverrideCreditGlAccountId
                        ?? await _mappingService.GetMappedAccountAsync(
                            refund.CompanyId,
                            SystemTransactionType.ReceiptRefundCash,
                            isDebit: false,
                            defaultAccountId: refund.BankAccountId.Value);

                    if (debitArAccount == Guid.Empty)
                        return "Posting Aborted: Customer Accounts Receivable account mapping is missing.";

                    decimal cashRefundBase = Math.Round(refund.TotalAmount * rate, 2);

                    if (cashRefundBase > 0)
                    {
                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = debitArAccount,
                            Debit = cashRefundBase,
                            Credit = 0,
                            Reference = $"Cash Refund Claim: {so.OrderNumber}"
                        });

                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = creditDisbursingAccount,
                            Debit = 0,
                            Credit = cashRefundBase,
                            Reference = $"Cash Out To: {refund.Customer?.Name}"
                        });
                    }
                }

                if (!glLines.Any()) return "No valid transaction elements or quantities were processed.";

                decimal totalDebits = glLines.Sum(l => l.Debit);
                decimal totalCredits = glLines.Sum(l => l.Credit);
                if (totalDebits != totalCredits)
                {
                    return $"Posting Aborted: Ledger imbalance. Debits ({totalDebits:N2}) do not match Credits ({totalCredits:N2}).";
                }

                var (err1, b1) = await _glOps.CreateJournalEntryAsync(refund.CompanyId, refund.Date, "Receipt Return Refund", refund.RefundNumber, glLines, userId.ToString(), existingBatchId: refund.GlBatchId);
                if (!string.IsNullOrEmpty(err1)) throw new Exception(err1);
                if (b1.HasValue) await _glOps.PostBatchAsync(refund.CompanyId, b1.Value, userId.ToString());

                refund.Status = ReceiptRefundStatus.Posted;
                refund.PostedAt = DateTime.UtcNow;
                refund.PostedByUserId = userId;
                refund.GlBatchId = b1;

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Fulfillment Return Error: {ex.Message}";
            }
        }

        public async Task<CashRefundSummaryDto> GetCashRefundSummaryAsync(Guid orderId, Guid currentRefundId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            decimal totalPaid = await ctx.PaymentApplications
                .Where(pa => pa.InvoiceId == orderId)
                .SumAsync(pa => pa.AppliedAmount + pa.CashDiscountTaken);

            decimal priorRefunded = await ctx.ReceiptRefunds
                .Where(r => r.SalesOrderId == orderId
                         && r.Id != currentRefundId
                         && r.Status == ReceiptRefundStatus.Posted
                         && (!r.GlBatchId.HasValue || ctx.GLBatches.Any(b => b.Id == r.GlBatchId.Value && b.Status == BatchStatus.Posted))
                         && (r.RefundType == ReceiptRefundType.PaymentOnly || r.RefundType == ReceiptRefundType.Both))
                .SumAsync(r => r.TotalAmount);

            return new CashRefundSummaryDto
            {
                TotalPaid = totalPaid,
                PriorCashRefunded = priorRefunded,
                MaxRefundable = Math.Max(0, totalPaid - priorRefunded)
            };
        }
    }

    public class CashRefundSummaryDto
    {
        public decimal TotalPaid { get; set; }
        public decimal PriorCashRefunded { get; set; }
        public decimal MaxRefundable { get; set; }
    }
}
