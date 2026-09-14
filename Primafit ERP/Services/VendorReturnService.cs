using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class VendorReturnService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;
        private readonly TransactionMappingService _mappingService;

        public VendorReturnService(
            IDbContextFactory<AppDbContext> dbFactory,
            GLOperationsService glOps,
            TransactionMappingService mappingService)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
            _mappingService = mappingService;
        }

        // =========================================================================
        // 1. DATA LOOKUP FILTERS & CEILING COMPUTATIONS
        // =========================================================================

        public async Task<List<PurchaseOrder>> GetOrdersEligibleForReturnAsync(Guid companyId, Guid vendorId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var validStatuses = new[] {
                PurchaseOrderStatus.Open,
                PurchaseOrderStatus.PartiallyReceived,
                PurchaseOrderStatus.DraftInvoice,
                PurchaseOrderStatus.Invoiced
            };

            var orders = await ctx.PurchaseOrders
                .Include(o => o.Currency)
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId
                         && o.VendorId == vendorId
                         && (o.HasReceipt || o.IsInvoicePosted || validStatuses.Contains(o.Status)))
                .ToListAsync();

            if (!orders.Any()) return new List<PurchaseOrder>();

            var orderIds = orders.Select(o => o.Id).ToList();

            var paymentsMap = await (from p in ctx.Set<VendorPayment>()
                                     join b in ctx.VendorBills on p.VendorBillId equals b.Id
                                     join batch in ctx.GLBatches on p.GLBatchId equals batch.Id into paymentBatches
                                     from batch in paymentBatches.DefaultIfEmpty()
                                     where b.PurchaseOrderId.HasValue
                                           && orderIds.Contains(b.PurchaseOrderId.Value)
                                           && b.IsPosted
                                           // New payments become refundable only after their
                                           // review batch is approved. Null batch supports legacy data.
                                           && (!p.GLBatchId.HasValue || batch.Status == BatchStatus.Posted)
                                           && p.Amount > 0
                                     group p by b.PurchaseOrderId!.Value into g
                                     select new { OrderId = g.Key, TotalPaidBase = g.Sum(x => x.Amount) })
                                    .ToDictionaryAsync(x => x.OrderId, x => x.TotalPaidBase);

            var historicalCashRefundsMap = await ctx.VendorReturns
                .Where(r => r.PurchaseOrderId.HasValue
                         && orderIds.Contains(r.PurchaseOrderId.Value)
                         && r.Status == VendorReturnStatus.Posted
                         && (r.ReturnType == VendorReturnType.PaymentOnly || r.ReturnType == VendorReturnType.Both))
                .GroupBy(r => r.PurchaseOrderId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.TotalAmount));

            var poLineIds = orders.SelectMany(o => o.Lines).Select(l => l.Id).ToList();
            var totalReceivedMap = await ctx.GoodsReceiptLines
                .Where(grl => poLineIds.Contains(grl.PurchaseOrderLineId))
                .GroupBy(grl => grl.PurchaseOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.QuantityReceived));

            var historicalQtyReturnsMap = await ctx.VendorReturnLines
                .Include(l => l.Header)
                .Where(l => l.Header!.PurchaseOrderId.HasValue
                         && orderIds.Contains(l.Header.PurchaseOrderId.Value)
                         && l.Header.Status == VendorReturnStatus.Posted
                         && (l.Header.ReturnType == VendorReturnType.QuantityOnly || l.Header.ReturnType == VendorReturnType.Both))
                .GroupBy(l => l.PurchaseOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var eligibleList = new List<PurchaseOrder>();

            foreach (var po in orders)
            {
                decimal rate = po.ExchangeRate > 0 ? po.ExchangeRate : 1;
                decimal paidBase = paymentsMap.TryGetValue(po.Id, out var pAmt) ? pAmt : 0;
                decimal paidForeign = Math.Round(paidBase / rate, 2);
                decimal refundedForeign = historicalCashRefundsMap.TryGetValue(po.Id, out var refAmt) ? refAmt : 0;
                decimal remainingCashLimit = paidForeign - refundedForeign;

                bool hasReturnableStock = false;
                foreach (var line in po.Lines)
                {
                    decimal totalReceived = totalReceivedMap.TryGetValue(line.Id, out var rQty) ? rQty : 0;
                    decimal previouslyReturned = historicalQtyReturnsMap.TryGetValue(line.Id, out var retQty) ? retQty : 0;
                    if (totalReceived - previouslyReturned > 0.001m)
                    {
                        hasReturnableStock = true;
                        break;
                    }
                }

                if (remainingCashLimit > 0.01m || hasReturnableStock)
                {
                    po.GrandTotalForeign = Math.Max(0, remainingCashLimit);
                    eligibleList.Add(po);
                }
            }

            return eligibleList.OrderByDescending(o => o.OrderDate).ToList();
        }

        // =========================================================================
        // 2. WORKSPACE CREATION & DRAFT RETRIEVAL
        // =========================================================================

        public async Task<VendorReturn> CreateVendorReturnDraftAsync(Guid orderId, VendorReturnType type, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var po = await ctx.PurchaseOrders
                .Include(s => s.Lines)
                .Include(s => s.Currency)
                .FirstOrDefaultAsync(s => s.Id == orderId);

            if (po == null) throw new Exception("Target purchase order reference missing from database context.");

            var poLineIds = po.Lines.Select(l => l.Id).ToList();
            var totalReceivedMap = await ctx.GoodsReceiptLines
                .Where(grl => poLineIds.Contains(grl.PurchaseOrderLineId))
                .GroupBy(grl => grl.PurchaseOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.QuantityReceived));

            var historicalQtyReturnsMap = await ctx.VendorReturnLines
                .Include(l => l.Header)
                .Where(l => l.Header!.PurchaseOrderId == orderId && l.Header.Status == VendorReturnStatus.Posted)
                .GroupBy(l => l.PurchaseOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

            var vendorReturn = new VendorReturn
            {
                Id = Guid.NewGuid(),
                CompanyId = po.CompanyId,
                PurchaseOrderId = po.Id,
                VendorId = po.VendorId,
                CurrencyId = po.CurrencyId,
                ExchangeRate = po.ExchangeRate,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = VendorReturnStatus.Draft,
                ReturnType = type,
                Reason = "Vendor Goods / Payment Return Workspace",
                WarehouseId = null,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                ReturnNumber = $"VRN-{DateTime.UtcNow:yyMM}-{Random.Shared.Next(1000, 9999)}"
            };

            foreach (var poLine in po.Lines)
            {
                decimal totalReceived = totalReceivedMap.TryGetValue(poLine.Id, out var rec) ? rec : 0;
                decimal alreadyReturned = historicalQtyReturnsMap.TryGetValue(poLine.Id, out var ret) ? ret : 0;
                decimal maxReturnable = Math.Max(0, totalReceived - alreadyReturned);

                if (type == VendorReturnType.PaymentOnly || maxReturnable > 0)
                {
                    vendorReturn.Lines.Add(new VendorReturnLine
                    {
                        Id = Guid.NewGuid(),
                        HeaderId = vendorReturn.Id,
                        ItemId = poLine.ItemId,
                        UomId = poLine.UomId,
                        UomName = poLine.UomName,
                        UomConversionFactor = poLine.UomConversionFactor,
                        PurchaseOrderLineId = poLine.Id,
                        Quantity = 0,
                        UnitCost = poLine.UnitCost,
                        OriginalReceivedQty = totalReceived,
                        PreviouslyReturnedQty = alreadyReturned,
                        MaxReturnableQty = maxReturnable
                    });
                }
            }

            ctx.VendorReturns.Add(vendorReturn);
            await ctx.SaveChangesAsync();
            return vendorReturn;
        }

        public async Task<VendorReturn?> GetByIdAsync(Guid id, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var vReturn = await ctx.VendorReturns
                .Include(r => r.Lines).ThenInclude(l => l.Item)
                .Include(r => r.Vendor)
                .Include(r => r.Currency)
                .Include(r => r.Warehouse)
                .Include(r => r.BankAccount)
                .Include(r => r.CustomTransactionType)
                .Include(r => r.PurchaseOrder).ThenInclude(po => po.Lines)
                .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

            if (vReturn != null && vReturn.PurchaseOrder != null)
            {
                var po = vReturn.PurchaseOrder;
                var poLineIds = po.Lines.Select(l => l.Id).ToList();

                var totalReceivedMap = await ctx.GoodsReceiptLines
                    .Where(grl => poLineIds.Contains(grl.PurchaseOrderLineId))
                    .GroupBy(grl => grl.PurchaseOrderLineId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.QuantityReceived));

                var historicalQtyReturnsMap = await ctx.VendorReturnLines
                    .Include(l => l.Header)
                    .Where(l => l.Header!.PurchaseOrderId == vReturn.PurchaseOrderId
                             && l.Header.Id != vReturn.Id
                             && l.Header.Status == VendorReturnStatus.Posted)
                    .GroupBy(l => l.PurchaseOrderLineId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                foreach (var line in vReturn.Lines)
                {
                    decimal totalReceived = totalReceivedMap.TryGetValue(line.PurchaseOrderLineId, out var rec) ? rec : 0;
                    decimal alreadyReturned = historicalQtyReturnsMap.TryGetValue(line.PurchaseOrderLineId, out var ret) ? ret : 0;

                    line.OriginalReceivedQty = totalReceived;
                    line.PreviouslyReturnedQty = alreadyReturned;
                    line.MaxReturnableQty = Math.Max(0, totalReceived - alreadyReturned);
                }
            }

            return vReturn;
        }

        // =========================================================================
        // 3. POSTING ENGINE: DEDICATED SEPARATE TRANSACTION TYPES
        // =========================================================================

        public async Task<string> PostVendorReturnAsync(Guid returnId, Guid userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var transaction = await ctx.Database.BeginTransactionAsync();
            try
            {
                var vReturn = await ctx.VendorReturns
                    .Include(r => r.Lines).ThenInclude(l => l.Item)
                    .Include(r => r.Vendor)
                    .Include(r => r.PurchaseOrder).ThenInclude(po => po.Lines)
                    .FirstOrDefaultAsync(r => r.Id == returnId);

                if (vReturn == null) return "Vendor return parameters not found.";
                if (vReturn.Status == VendorReturnStatus.Posted)
                {
                    var existingBatch = vReturn.GlBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == vReturn.GlBatchId.Value && b.CompanyId == vReturn.CompanyId)
                        : null;
                    if (existingBatch?.Status == BatchStatus.Posted) return "Document is already committed to the General Ledger.";
                    if (existingBatch == null) return "Document is already locked and has no review batch.";
                }
                if (vReturn.PurchaseOrder == null) return "Parent purchase order reference mapping is missing.";

                var po = vReturn.PurchaseOrder;
                var glLines = new List<GLJournalLine>();
                decimal rate = vReturn.ExchangeRate > 0 ? vReturn.ExchangeRate : 1;

                TransactionGlMapping? customMapping = null;
                if (vReturn.CustomTransactionTypeId.HasValue)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == vReturn.CompanyId && m.CustomTransactionTypeId == vReturn.CustomTransactionTypeId.Value);
                }

                var vendor = await ctx.Vendors.FindAsync(vReturn.VendorId);
                Guid defaultApAccount = vendor?.PayablesAccountId ?? Guid.Empty;

                // -----------------------------------------------------------------
                // LEG A: PHYSICAL STOCK RETURN (SystemTransactionType.VendorReturnStock)
                // DEBIT: AP Trade Liability | CREDIT: Inventory Asset
                // -----------------------------------------------------------------
                if (vReturn.ReturnType == VendorReturnType.QuantityOnly || vReturn.ReturnType == VendorReturnType.Both)
                {
                    if (!vReturn.WarehouseId.HasValue || vReturn.WarehouseId == Guid.Empty)
                        return "Posting Aborted: Source warehouse location is required for physical inventory returns.";

                    // Resolve Accounts Payable Account (Debit side - reduces debt balance)
                    Guid debitApAccount = vReturn.OverrideAccountsPayableGlAccountId
                        ?? customMapping?.OverrideDebitGlAccountId
                        ?? po.AccountsPayableGlAccountId
                        ?? await _mappingService.GetMappedAccountAsync(
                            vReturn.CompanyId,
                            SystemTransactionType.VendorReturnStock,
                            isDebit: true,
                            defaultAccountId: defaultApAccount);

                    if (debitApAccount == Guid.Empty)
                        return "Posting Aborted: Missing Accounts Payable (AP) GL Account mapping for Stock Return.";

                    var poLineIds = po.Lines.Select(l => l.Id).ToList();
                    var totalReceivedMap = await ctx.GoodsReceiptLines
                        .Where(grl => poLineIds.Contains(grl.PurchaseOrderLineId))
                        .GroupBy(grl => grl.PurchaseOrderLineId)
                        .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.QuantityReceived));

                    var historicalQtyReturnsMap = await ctx.VendorReturnLines
                        .Include(l => l.Header)
                        .Where(l => l.Header!.PurchaseOrderId == po.Id
                                 && l.Header.Id != vReturn.Id
                                 && l.Header.Status == VendorReturnStatus.Posted)
                        .GroupBy(l => l.PurchaseOrderLineId)
                        .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Quantity));

                    foreach (var line in vReturn.Lines)
                    {
                        if (line.Quantity <= 0 || line.Item == null) continue;

                        var poLine = po.Lines.FirstOrDefault(pl => pl.Id == line.PurchaseOrderLineId);
                        if (poLine == null) return $"Line mapping error for product reference {line.Item.Name}.";

                        decimal totalRec = totalReceivedMap.TryGetValue(line.PurchaseOrderLineId, out var rQty) ? rQty : 0;
                        decimal alreadyRet = historicalQtyReturnsMap.TryGetValue(line.PurchaseOrderLineId, out var q) ? q : 0;
                        decimal maxAllowedReturn = totalRec - alreadyRet;

                        if (line.Quantity > maxAllowedReturn + 0.001m)
                            return $"Posting Aborted: Item '{line.Item.Name}' return quantity ({line.Quantity:N2}) exceeds remaining received allowance ({maxAllowedReturn:N2}).";

                        decimal currentWhStock = await ctx.StockLedgers
                            .Where(s => s.ItemId == line.ItemId && s.WarehouseId == vReturn.WarehouseId.Value)
                            .SumAsync(s => s.QuantityChanged);

                        if (currentWhStock < line.Quantity)
                            return $"Posting Aborted: Insufficient warehouse stock for '{line.Item.Name}'. In Stock: {currentWhStock:N2}, Attempting Return: {line.Quantity:N2}.";

                        // 1. Stock Ledger Outflow
                        ctx.StockLedgers.Add(new StockLedger
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = vReturn.CompanyId,
                            ItemId = line.ItemId,
                            WarehouseId = vReturn.WarehouseId.Value,
                            QuantityChanged = -line.Quantity,
                            Type = StockMovementType.PurchaseReturn,
                            CostAtTime = poLine.UnitCost,
                            Reference = vReturn.ReturnNumber,
                            Date = DateTime.UtcNow
                        });

                        // 2. Direct Double Entry: DR Accounts Payable (Liability) | CR Inventory Asset
                        decimal lineStockValueBase = Math.Round(line.Quantity * poLine.UnitCost * rate, 2);

                        if (lineStockValueBase > 0)
                        {
                            Guid defaultInventoryAsset = line.Item.InventoryAssetAccountId;

                            Guid creditAssetAccount = vReturn.OverrideInventoryAssetGlAccountId
                                ?? customMapping?.OverrideCreditGlAccountId
                                ?? await _mappingService.GetMappedAccountAsync(
                                    vReturn.CompanyId,
                                    SystemTransactionType.VendorReturnStock,
                                    isDebit: false,
                                    defaultAccountId: defaultInventoryAsset);

                            if (creditAssetAccount == Guid.Empty)
                                return $"Posting Aborted: Item '{line.Item.Name}' is missing an Inventory Asset GL Account mapping.";

                            glLines.Add(new GLJournalLine
                            {
                                SegCoaId = debitApAccount,
                                Debit = lineStockValueBase,
                                Credit = 0,
                                Reference = $"AP Liability Reduction: {line.Item.SKU}"
                            });

                            glLines.Add(new GLJournalLine
                            {
                                SegCoaId = creditAssetAccount,
                                Debit = 0,
                                Credit = lineStockValueBase,
                                Reference = $"Stock Outflow (Return): {line.Item.SKU}"
                            });
                        }

                        poLine.QuantityReceived = Math.Max(0, poLine.QuantityReceived - line.Quantity);
                    }
                }

                // -----------------------------------------------------------------
                // LEG B: CASH PAYMENT REFUND (SystemTransactionType.VendorReturnRefund)
                // DEBIT: Bank / Cash Asset | CREDIT: Accounts Payable
                // -----------------------------------------------------------------
                if (vReturn.ReturnType == VendorReturnType.PaymentOnly || vReturn.ReturnType == VendorReturnType.Both)
                {
                    if (!vReturn.BankAccountId.HasValue || vReturn.BankAccountId.Value == Guid.Empty)
                        return "Posting Aborted: Bank / Cash deposit account is required for cash refund recovery.";

                    Guid creditApAccount = vReturn.OverrideAccountsPayableGlAccountId
                        ?? customMapping?.OverrideCreditGlAccountId
                        ?? await _mappingService.GetMappedAccountAsync(
                            vReturn.CompanyId,
                            SystemTransactionType.VendorReturnRefund,
                            isDebit: false,
                            defaultAccountId: defaultApAccount);

                    if (creditApAccount == Guid.Empty)
                        return "Posting Aborted: Vendor Accounts Payable (AP) GL account mapping is unassigned.";

                    // Recalculate the cash ceiling from approved payments at posting time.
                    // The UI value is only a convenience and must not be trusted for accounting.
                    var approvedPaymentTotals = await (from p in ctx.Set<VendorPayment>()
                                                       join b in ctx.VendorBills on p.VendorBillId equals b.Id
                                                       join batch in ctx.GLBatches on p.GLBatchId equals batch.Id into paymentBatches
                                                       from batch in paymentBatches.DefaultIfEmpty()
                                                       where b.PurchaseOrderId == po.Id
                                                             && b.IsPosted
                                                             && p.Amount > 0
                                                             && (!p.GLBatchId.HasValue || batch.Status == BatchStatus.Posted)
                                                       group p by b.PurchaseOrderId into g
                                                       select new
                                                       {
                                                           CashPaidBase = g.Sum(x => x.Amount),
                                                           GrossSettledBase = g.Sum(x => x.Amount + x.WithholdingAmount)
                                                       }).FirstOrDefaultAsync();

                    decimal approvedCashPaidForeign = Math.Round((approvedPaymentTotals?.CashPaidBase ?? 0) / rate, 4);
                    decimal priorRefundedForeign = await ctx.VendorReturns
                        .Where(r => r.PurchaseOrderId == po.Id
                                 && r.Id != vReturn.Id
                                 && r.Status == VendorReturnStatus.Posted
                                 && (r.ReturnType == VendorReturnType.PaymentOnly || r.ReturnType == VendorReturnType.Both))
                        .SumAsync(r => r.TotalAmount);
                    decimal maxRefundableForeign = Math.Max(0, approvedCashPaidForeign - priorRefundedForeign);

                    if (vReturn.TotalAmount <= 0)
                        return "Posting Aborted: Cash refund amount must be greater than zero.";
                    if (vReturn.TotalAmount > maxRefundableForeign + 0.0001m)
                        return $"Posting Aborted: Cash refund ({vReturn.TotalAmount:N4}) exceeds the remaining cash actually paid ({maxRefundableForeign:N4}). The limit is net of withholding tax.";

                    decimal cashRefundBase = Math.Round(vReturn.TotalAmount * rate, 4);

                    if (cashRefundBase > 0)
                    {
                        // DR: Bank / Cash Asset (Inflow)
                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = vReturn.BankAccountId.Value,
                            Debit = cashRefundBase,
                            Credit = 0,
                            Reference = $"Vendor Cash Refund: {vReturn.ReturnNumber}"
                        });

                        // CR: Accounts Payable (Restores AP debt balance)
                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = creditApAccount,
                            Debit = 0,
                            Credit = cashRefundBase,
                            Reference = $"AP Balance Restored: {po.OrderNumber}"
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

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                    vReturn.CompanyId,
                    vReturn.Date,
                    "Vendor Return",
                    vReturn.ReturnNumber,
                    glLines,
                    userId.ToString(),
                    existingBatchId: vReturn.GlBatchId);

                if (!string.IsNullOrEmpty(err)) throw new Exception(err);
                if (batchId.HasValue) await _glOps.PostBatchAsync(vReturn.CompanyId, batchId.Value, userId.ToString());

                vReturn.Status = VendorReturnStatus.Posted;
                vReturn.PostedAt = DateTime.UtcNow;
                vReturn.PostedByUserId = userId;
                vReturn.GlBatchId = batchId;

                await ctx.SaveChangesAsync();
                await transaction.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"Vendor Return Error: {ex.Message}";
            }
        }

        // =========================================================================
        // 4. SHARED DRAFT MANAGEMENT WORKFLOWS
        // =========================================================================

        public async Task<string> SaveDraftAsync(VendorReturn vReturn)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.VendorReturns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == vReturn.Id);

            if (existing == null) return "Vendor return tracking record not found.";
            var existingBatch = existing.GlBatchId.HasValue
                ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == existing.GlBatchId.Value && b.CompanyId == existing.CompanyId)
                : null;
            if (existing.Status == VendorReturnStatus.Posted && (existingBatch == null || existingBatch.Status == BatchStatus.Posted))
                return "Cannot edit locked records.";

            existing.Date = vReturn.Date;
            existing.TransactionDateTime = vReturn.TransactionDateTime;
            existing.Reason = vReturn.Reason;
            existing.ReturnType = vReturn.ReturnType;
            existing.BankAccountId = vReturn.BankAccountId;
            existing.WarehouseId = vReturn.WarehouseId;
            existing.TotalAmount = vReturn.TotalAmount;
            existing.CustomTransactionTypeId = vReturn.CustomTransactionTypeId;
            existing.OverrideGrIrClearingGlAccountId = vReturn.OverrideGrIrClearingGlAccountId;
            existing.OverrideInventoryAssetGlAccountId = vReturn.OverrideInventoryAssetGlAccountId;
            existing.OverrideAccountsPayableGlAccountId = vReturn.OverrideAccountsPayableGlAccountId;

            ctx.VendorReturnLines.RemoveRange(existing.Lines);

            foreach (var line in vReturn.Lines)
            {
                ctx.VendorReturnLines.Add(new VendorReturnLine
                {
                    Id = Guid.NewGuid(),
                    HeaderId = existing.Id,
                    ItemId = line.ItemId,
                    UomId = line.UomId,
                    UomName = line.UomName,
                    UomConversionFactor = line.UomConversionFactor,
                    PurchaseOrderLineId = line.PurchaseOrderLineId,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost
                });
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> DeleteDraftAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.VendorReturns.FindAsync(id);

            if (existing == null || existing.Status != VendorReturnStatus.Draft)
                return "Invalid draft record.";

            ctx.VendorReturns.Remove(existing);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<VendorCashRefundSummaryDto> GetCashRefundSummaryAsync(Guid orderId, Guid currentReturnId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var po = await ctx.PurchaseOrders.FindAsync(orderId);
            decimal rate = po?.ExchangeRate > 0 ? po.ExchangeRate : 1;

            var approvedPaymentTotals = await (from p in ctx.Set<VendorPayment>()
                                               join b in ctx.VendorBills on p.VendorBillId equals b.Id
                                               join batch in ctx.GLBatches on p.GLBatchId equals batch.Id into paymentBatches
                                               from batch in paymentBatches.DefaultIfEmpty()
                                               where b.PurchaseOrderId == orderId
                                                     && b.IsPosted
                                                     && p.Amount > 0
                                                     && (!p.GLBatchId.HasValue || batch.Status == BatchStatus.Posted)
                                               group p by b.PurchaseOrderId into g
                                               select new
                                               {
                                                   CashPaidBase = g.Sum(x => x.Amount),
                                                   GrossSettledBase = g.Sum(x => x.Amount + x.WithholdingAmount),
                                                   WithholdingPaidBase = g.Sum(x => x.WithholdingAmount)
                                               }).FirstOrDefaultAsync();

            decimal totalPaidForeign = Math.Round((approvedPaymentTotals?.CashPaidBase ?? 0) / rate, 4);
            decimal grossSettledForeign = Math.Round((approvedPaymentTotals?.GrossSettledBase ?? 0) / rate, 4);
            decimal withholdingPaidForeign = Math.Round((approvedPaymentTotals?.WithholdingPaidBase ?? 0) / rate, 4);

            decimal priorRefunded = await ctx.VendorReturns
                .Where(r => r.PurchaseOrderId == orderId
                         && r.Id != currentReturnId
                         && r.Status == VendorReturnStatus.Posted
                         && (r.ReturnType == VendorReturnType.PaymentOnly || r.ReturnType == VendorReturnType.Both))
                .SumAsync(r => r.TotalAmount);

            return new VendorCashRefundSummaryDto
            {
                TotalPaid = totalPaidForeign,
                GrossSettlementPaid = grossSettledForeign,
                WithholdingPaid = withholdingPaidForeign,
                PriorCashRefunded = priorRefunded,
                MaxRefundable = Math.Max(0, totalPaidForeign - priorRefunded)
            };
        }
    }

    // AP-only summary. This is deliberately separate from the AR/Sales
    // receipt-refund summary so withholding-tax data cannot enter that module.
    public class VendorCashRefundSummaryDto
    {
        public decimal TotalPaid { get; set; }
        public decimal GrossSettlementPaid { get; set; }
        public decimal WithholdingPaid { get; set; }
        public decimal PriorCashRefunded { get; set; }
        public decimal MaxRefundable { get; set; }
    }
}
