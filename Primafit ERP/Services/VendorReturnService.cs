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

            // Total base payments applied to posted bills linked to these purchase orders
            var paymentsMap = await (from p in ctx.Set<VendorPayment>()
                                     join b in ctx.VendorBills on p.VendorBillId equals b.Id
                                     where b.PurchaseOrderId.HasValue
                                           && orderIds.Contains(b.PurchaseOrderId.Value)
                                           && b.IsPosted
                                           && p.Amount > 0
                                     group p by b.PurchaseOrderId!.Value into g
                                     select new { OrderId = g.Key, TotalPaidBase = g.Sum(x => x.Amount) })
                                    .ToDictionaryAsync(x => x.OrderId, x => x.TotalPaidBase);

            // Historical cash refunds posted under VendorReturn
            var historicalCashRefundsMap = await ctx.VendorReturns
                .Where(r => r.PurchaseOrderId.HasValue
                         && orderIds.Contains(r.PurchaseOrderId.Value)
                         && r.Status == VendorReturnStatus.Posted
                         && (r.ReturnType == VendorReturnType.PaymentOnly || r.ReturnType == VendorReturnType.Both))
                .GroupBy(r => r.PurchaseOrderId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.TotalAmount));

            // Actual physical received quantities from GoodsReceipt
            var poLineIds = orders.SelectMany(o => o.Lines).Select(l => l.Id).ToList();
            var totalReceivedMap = await ctx.GoodsReceiptLines
                .Where(grl => poLineIds.Contains(grl.PurchaseOrderLineId))
                .GroupBy(grl => grl.PurchaseOrderLineId)
                .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.QuantityReceived));

            // Previously returned stock quantities
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
        // 3. POSTING ENGINE (BALANCED DOUBLE-ENTRY LEDGER TRANSACTIONS)
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
                if (vReturn.Status == VendorReturnStatus.Posted) return "Document is already posted and locked.";
                if (vReturn.PurchaseOrder == null) return "Parent purchase order reference mapping is missing.";

                var po = vReturn.PurchaseOrder;
                var glLines = new List<GLJournalLine>();
                decimal rate = vReturn.ExchangeRate > 0 ? vReturn.ExchangeRate : 1;

                // -----------------------------------------------------------------
                // LEG A: PHYSICAL STOCK RETURN (REVERSES GOODS RECEIPT AT PO COST)
                // -----------------------------------------------------------------
                if (vReturn.ReturnType == VendorReturnType.QuantityOnly || vReturn.ReturnType == VendorReturnType.Both)
                {
                    if (!vReturn.WarehouseId.HasValue || vReturn.WarehouseId == Guid.Empty)
                        return "Posting Aborted: Source warehouse location is required to log physical inventory returns.";

                    // Look up GR/IR Clearing Account used during Goods Receipt
                    var grns = await ctx.GoodsReceipts
                        .AsNoTracking()
                        .Where(g => g.PurchaseOrderId == po.Id && g.CompanyId == vReturn.CompanyId)
                        .ToListAsync();

                    Guid grIrAccountId = grns.FirstOrDefault(g => g.InventoryGlAccountId != Guid.Empty)?.InventoryGlAccountId ?? Guid.Empty;

                    if (grIrAccountId == Guid.Empty)
                    {
                        grIrAccountId = await _mappingService.GetMappedAccountAsync(
                            vReturn.CompanyId,
                            SystemTransactionType.GoodsReceipt,
                            isDebit: false,
                            defaultAccountId: Guid.Empty);
                    }

                    if (grIrAccountId == Guid.Empty)
                        return "Posting Aborted: Missing GR/IR Clearing GL Account mapping.";

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
                        if (line.Quantity <= 0) continue;
                        if (line.Item == null) continue;

                        var poLine = po.Lines.FirstOrDefault(pl => pl.Id == line.PurchaseOrderLineId);
                        if (poLine == null) return $"Line mapping error for product reference {line.Item.Name}.";

                        decimal totalRec = totalReceivedMap.TryGetValue(line.PurchaseOrderLineId, out var rQty) ? rQty : 0;
                        decimal alreadyRet = historicalQtyReturnsMap.TryGetValue(line.PurchaseOrderLineId, out var q) ? q : 0;
                        decimal maxAllowedReturn = totalRec - alreadyRet;

                        if (line.Quantity > maxAllowedReturn + 0.001m)
                            return $"Posting Aborted: Item '{line.Item.Name}' quantity returned ({line.Quantity:N2}) exceeds remaining received allowance ({maxAllowedReturn:N2}).";

                        // Verify physical warehouse on-hand stock
                        decimal currentWhStock = await ctx.StockLedgers
                            .Where(s => s.ItemId == line.ItemId && s.WarehouseId == vReturn.WarehouseId.Value)
                            .SumAsync(s => s.QuantityChanged);

                        if (currentWhStock < line.Quantity)
                            return $"Posting Aborted: Insufficient stock for '{line.Item.Name}'. Available in warehouse: {currentWhStock:N2}, Trying to return: {line.Quantity:N2}.";

                        // 1. Deduct Stock from Warehouse Ledger at PO Unit Cost
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

                        // 2. Exact Goods Receipt Reversal: DR GR/IR Clearing | CR Inventory Asset
                        decimal lineStockValueBase = Math.Round(line.Quantity * poLine.UnitCost * rate, 2);

                        if (lineStockValueBase > 0)
                        {
                            Guid assetAccount = line.Item.InventoryAssetAccountId;
                            if (assetAccount == Guid.Empty)
                                return $"Posting Aborted: Item '{line.Item.Name}' is missing Inventory Asset GL Account mapping.";

                            glLines.Add(new GLJournalLine
                            {
                                SegCoaId = grIrAccountId,
                                Debit = lineStockValueBase,
                                Credit = 0,
                                Reference = $"GR/IR Return Reversal: {line.Item.SKU}"
                            });

                            glLines.Add(new GLJournalLine
                            {
                                SegCoaId = assetAccount,
                                Debit = 0,
                                Credit = lineStockValueBase,
                                Reference = $"Stock Outflow: {line.Item.SKU}"
                            });
                        }

                        // Decrement received counter on PO line
                        poLine.QuantityReceived = Math.Max(0, poLine.QuantityReceived - line.Quantity);
                    }
                }

                // -----------------------------------------------------------------
                // LEG B: CASH PAYMENT REFUND (REVERSES VENDOR PAYMENT)
                // -----------------------------------------------------------------
                if (vReturn.ReturnType == VendorReturnType.PaymentOnly || vReturn.ReturnType == VendorReturnType.Both)
                {
                    if (!vReturn.BankAccountId.HasValue || vReturn.BankAccountId.Value == Guid.Empty)
                        return "Posting Aborted: Bank / Cash deposit account is required for cash refund recovery.";

                    var vendor = await ctx.Vendors.FindAsync(vReturn.VendorId);
                    Guid defaultApAccount = vendor?.PayablesAccountId ?? Guid.Empty;

                    // isDebit: true targets the AP control account override from GL mapping
                    Guid apAccount = await _mappingService.GetMappedAccountAsync(
                        vReturn.CompanyId,
                        SystemTransactionType.ReturnToVendor,
                        isDebit: true,
                        defaultAccountId: defaultApAccount);

                    if (apAccount == Guid.Empty)
                        return "Posting Aborted: Vendor Accounts Payable (AP) GL account mapping is unassigned.";

                    decimal cashRefundBase = Math.Round(vReturn.TotalAmount * rate, 2);

                    if (cashRefundBase > 0)
                    {
                        // DR: Bank / Cash Account (Refunding liquid asset)
                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = vReturn.BankAccountId.Value,
                            Debit = cashRefundBase,
                            Credit = 0,
                            Reference = $"Vendor Cash Refund: {vReturn.ReturnNumber}"
                        });

                        // CR: Accounts Payable (Restores outstanding liability balance)
                        glLines.Add(new GLJournalLine
                        {
                            SegCoaId = apAccount,
                            Debit = 0,
                            Credit = cashRefundBase,
                            Reference = $"AP Balance Restored: {po.OrderNumber}"
                        });
                    }
                }

                if (!glLines.Any()) return "No valid transaction elements or quantities were processed.";

                // Validation checksum gate
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
                    userId.ToString());

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
            if (existing.Status == VendorReturnStatus.Posted) return "Cannot edit locked records.";

            existing.Date = vReturn.Date;
            existing.Reason = vReturn.Reason;
            existing.ReturnType = vReturn.ReturnType;
            existing.BankAccountId = vReturn.BankAccountId;
            existing.WarehouseId = vReturn.WarehouseId;
            existing.TotalAmount = vReturn.TotalAmount;

            ctx.VendorReturnLines.RemoveRange(existing.Lines);

            foreach (var line in vReturn.Lines)
            {
                ctx.VendorReturnLines.Add(new VendorReturnLine
                {
                    Id = Guid.NewGuid(),
                    HeaderId = existing.Id,
                    ItemId = line.ItemId,
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

        public async Task<CashRefundSummaryDto> GetCashRefundSummaryAsync(Guid orderId, Guid currentReturnId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var po = await ctx.PurchaseOrders.FindAsync(orderId);
            decimal rate = po?.ExchangeRate > 0 ? po.ExchangeRate : 1;

            decimal totalPaidBase = await (from p in ctx.Set<VendorPayment>()
                                           join b in ctx.VendorBills on p.VendorBillId equals b.Id
                                           where b.PurchaseOrderId == orderId && b.IsPosted && p.Amount > 0
                                           select p.Amount).SumAsync();

            decimal totalPaidForeign = Math.Round(totalPaidBase / rate, 2);

            decimal priorRefunded = await ctx.VendorReturns
                .Where(r => r.PurchaseOrderId == orderId
                         && r.Id != currentReturnId
                         && r.Status == VendorReturnStatus.Posted
                         && (r.ReturnType == VendorReturnType.PaymentOnly || r.ReturnType == VendorReturnType.Both))
                .SumAsync(r => r.TotalAmount);

            return new CashRefundSummaryDto
            {
                TotalPaid = totalPaidForeign,
                PriorCashRefunded = priorRefunded,
                MaxRefundable = Math.Max(0, totalPaidForeign - priorRefunded)
            };
        }
    }
}