using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class DashboardService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public DashboardService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // ── Lightweight projection used to load GL data into memory ──────────
        private record GlRecord(Guid CoaId, decimal Dr, decimal Cr, DateOnly Date);

        public async Task<FinancialDashboardSnapshot> GetSnapshotAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var startOfYear = new DateTime(now.Year, 1, 1);
            var sixMonthsAgo = startOfMonth.AddMonths(-5);
            var startPrevMonth = startOfMonth.AddMonths(-1);

            var snapshot = new FinancialDashboardSnapshot { CompanyId = companyId };

            // ═══════════════════════════════════════════════════════════════════
            // 1. MASTER DATA  (COA + Account Types)
            // ═══════════════════════════════════════════════════════════════════
            var accountTypes = await ctx.Set<SegAccountType>().AsNoTracking().ToListAsync();
            var accounts = await ctx.SegChartOfAccounts.AsNoTracking()
                                   .Where(a => a.CompanyId == companyId && a.IsActive)
                                   .ToListAsync();

            var assetTypeIds = accountTypes.Where(t => t.IsBalanceSheet && t.IsDebit).Select(t => t.Id).ToHashSet();
            var liabTypeIds = accountTypes.Where(t => t.IsBalanceSheet && !t.IsDebit).Select(t => t.Id).ToHashSet();
            var revTypeIds = accountTypes.Where(t => !t.IsBalanceSheet && !t.IsDebit).Select(t => t.Id).ToHashSet();
            var expTypeIds = accountTypes.Where(t => !t.IsBalanceSheet && t.IsDebit).Select(t => t.Id).ToHashSet();

            var accountTypeMap = accounts.ToDictionary(a => a.Id, a => a.SegAccountTypeId);
            var accountDescMap = accounts.ToDictionary(a => a.Id, a => a.Description);
            var cogsAccountIds = accounts
                .Where(a => expTypeIds.Contains(a.SegAccountTypeId) &&
                            a.Description.Contains("Cost of Goods", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Id)
                .ToHashSet();

            // ═══════════════════════════════════════════════════════════════════
            // 2. OPERATIONAL COUNTS 
            // ═══════════════════════════════════════════════════════════════════
            snapshot.UnshippedOrdersCount = await ctx.SalesOrders
                .Where(o => o.CompanyId == companyId && o.OrderNumber.StartsWith("INV"))
                .CountAsync(o => o.Lines.Any(l => l.Item != null && !l.Item.IsService && l.QtyShipped < l.Quantity));

            snapshot.PendingTransfersCount = await ctx.StockTransfers
                .CountAsync(t => t.CompanyId == companyId && t.Status == TransferStatus.InTransit);

            snapshot.APExceptionsCount = await ctx.VendorBills
                .CountAsync(b => b.CompanyId == companyId && b.MatchStatus == BillMatchStatus.Variance);

            // ═══════════════════════════════════════════════════════════════════
            // 3. STOCK 
            // ═══════════════════════════════════════════════════════════════════
            var stockLevels = await ctx.StockLedgers
                .Where(s => s.CompanyId == companyId)
                .GroupBy(s => s.ItemId)
                .Select(g => new { g.Key, Qty = g.Sum(x => x.QuantityChanged) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty);

            var physicalItems = await ctx.Items.AsNoTracking()
                .Where(i => i.CompanyId == companyId && !i.IsService && i.ReorderLevel > 0)
                .ToListAsync();

            snapshot.LowStockItemsCount = physicalItems
                .Count(i => stockLevels.GetValueOrDefault(i.Id, 0) <= i.ReorderLevel);

            // ═══════════════════════════════════════════════════════════════════
            // 4. GL
            // ═══════════════════════════════════════════════════════════════════
            var allTimeBalances = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId)
                .GroupBy(t => t.SegCoaId)
                .Select(g => new { g.Key, Bal = g.Sum(t => t.Debit - t.Credit) })
                .ToDictionaryAsync(x => x.Key, x => x.Bal);

            var recentGl = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId &&
                            t.PostingDate >= DateOnly.FromDateTime(sixMonthsAgo))
                .Select(t => new GlRecord(t.SegCoaId, t.Debit, t.Credit, t.PostingDate))
                .ToListAsync();

            decimal SumAccounts(Func<SegChartOfAccount, bool> filter) =>
                accounts.Where(filter).Sum(a => allTimeBalances.GetValueOrDefault(a.Id, 0));

            var cashBal = SumAccounts(a => assetTypeIds.Contains(a.SegAccountTypeId) &&
                               (a.Description.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
                                a.Description.Contains("Cash", StringComparison.OrdinalIgnoreCase)));

            var invBal = SumAccounts(a => assetTypeIds.Contains(a.SegAccountTypeId) &&
                               a.Description.Contains("Inventory", StringComparison.OrdinalIgnoreCase));

            var totAssets = SumAccounts(a => assetTypeIds.Contains(a.SegAccountTypeId));
            var totLiabs = Math.Abs(SumAccounts(a => liabTypeIds.Contains(a.SegAccountTypeId)));

            snapshot.CashOnHand = cashBal;
            snapshot.InventoryValue = invBal;
            snapshot.TotalAssets = totAssets;

            bool IsRev(Guid id) => accountTypeMap.TryGetValue(id, out var t) && revTypeIds.Contains(t);
            bool IsExp(Guid id) => accountTypeMap.TryGetValue(id, out var t) && expTypeIds.Contains(t);
            bool IsCogs(Guid id) => cogsAccountIds.Contains(id);

            decimal RevOf(List<GlRecord> gl) => Math.Abs(gl.Where(t => IsRev(t.CoaId)).Sum(t => t.Cr - t.Dr));
            decimal ExpOf(List<GlRecord> gl) => gl.Where(t => IsExp(t.CoaId)).Sum(t => t.Dr - t.Cr);
            decimal CogsOf(List<GlRecord> gl) => gl.Where(t => IsCogs(t.CoaId)).Sum(t => t.Dr - t.Cr);

            var mtdGl = recentGl.Where(t => t.Date.Year == now.Year && t.Date.Month == now.Month).ToList();
            var ytdGl = recentGl.Where(t => t.Date >= DateOnly.FromDateTime(startOfYear)).ToList();
            var prevMonthGl = recentGl.Where(t => t.Date.Year == startPrevMonth.Year && t.Date.Month == startPrevMonth.Month).ToList();

            var revMtd = RevOf(mtdGl);
            var expMtd = ExpOf(mtdGl);
            var cogsMtd = CogsOf(mtdGl);

            snapshot.RevenueMTD = revMtd;
            snapshot.RevenueYTD = RevOf(ytdGl);
            snapshot.NetProfitMTD = revMtd - expMtd;
            snapshot.GrossProfitMargin = revMtd != 0 ? ((revMtd - cogsMtd) / revMtd) * 100 : 0;
            snapshot.NetProfitMargin = revMtd != 0 ? (snapshot.NetProfitMTD / revMtd) * 100 : 0;
            snapshot.InventoryTurnover = invBal != 0 ? cogsMtd / invBal : 0;
            snapshot.PreviousMonthRevenue = RevOf(prevMonthGl);
            snapshot.PreviousMonthProfit = RevOf(prevMonthGl) - ExpOf(prevMonthGl);

            // ═══════════════════════════════════════════════════════════════════
            // 5. SIX-MONTH TREND  
            // ═══════════════════════════════════════════════════════════════════
            for (int i = 0; i < 6; i++)
            {
                var m = sixMonthsAgo.AddMonths(i);
                var gl = recentGl.Where(t => t.Date.Year == m.Year && t.Date.Month == m.Month).ToList();
                snapshot.SixMonthCashFlow.Add(new MonthlyTrend
                {
                    Month = m.ToString("MMM yyyy"),
                    Revenue = RevOf(gl),
                    Expenses = ExpOf(gl)
                });
            }

            // ═══════════════════════════════════════════════════════════════════
            // 6. TOP EXPENSES MTD  
            // ═══════════════════════════════════════════════════════════════════
            snapshot.TopExpenses = mtdGl
                .Where(t => IsExp(t.CoaId))
                .GroupBy(t => t.CoaId)
                .Select(g => new ExpenseDistribution
                {
                    AccountName = accountDescMap.GetValueOrDefault(g.Key, "Unknown"),
                    Amount = g.Sum(t => t.Dr - t.Cr)
                })
                .Where(e => e.Amount > 0)
                .OrderByDescending(e => e.Amount)
                .Take(5)
                .ToList();

            // ═══════════════════════════════════════════════════════════════════
            // 7. AR / AP AGING
            // ═══════════════════════════════════════════════════════════════════
            var taxes = await ctx.Taxes.Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Per);

            var unpaidInvoices = await ctx.SalesOrders
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId &&
                            (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced))
                .AsNoTracking()
                .ToListAsync();

            var invoiceIds = unpaidInvoices.Select(o => o.Id).ToList();
            var arPayments = await ctx.PaymentApplications
                .Where(pa => invoiceIds.Contains(pa.InvoiceId))
                .GroupBy(pa => pa.InvoiceId)
                .Select(g => new { g.Key, Paid = g.Sum(x => x.AppliedAmount + x.CashDiscountTaken) })
                .ToDictionaryAsync(x => x.Key, x => x.Paid);

            foreach (var inv in unpaidInvoices)
            {
                decimal subTotal = inv.Lines.Sum(l => l.Quantity * l.UnitPrice);
                decimal discount = inv.DiscountPercentage > 0 ? subTotal * (inv.DiscountPercentage / 100) : inv.DiscountAmount;
                decimal netForeign = subTotal - discount;

                decimal taxPer = inv.TaxId.HasValue && taxes.ContainsKey(inv.TaxId.Value) ? taxes[inv.TaxId.Value] : 0;
                decimal grandTotalForeign = netForeign + (netForeign * (taxPer / 100));

                decimal rate = inv.ExchangeRate > 0 ? inv.ExchangeRate : 1;
                decimal grandTotalBase = grandTotalForeign * rate;

                decimal paidForeign = arPayments.GetValueOrDefault(inv.Id, 0);
                decimal paidBase = paidForeign * rate;

                decimal balBase = grandTotalBase - paidBase;
                if (balBase <= 0.01m) continue;

                int d = today.DayNumber - inv.Date.DayNumber;
                if (d <= 0) snapshot.ARAging.Current += balBase;
                else if (d <= 30) snapshot.ARAging.Days1To30 += balBase;
                else if (d <= 60) snapshot.ARAging.Days31To60 += balBase;
                else snapshot.ARAging.DaysOver60 += balBase;
            }

            var unpaidBills = await ctx.VendorBills.Include(b => b.Payments)
                .Where(b => b.CompanyId == companyId && b.IsPosted)
                .AsNoTracking()
                .ToListAsync();

            foreach (var bill in unpaidBills)
            {
                decimal balBase = bill.TotalAmount - bill.Payments.Sum(p => p.Amount);
                if (balBase <= 0.01m) continue;

                int d = today.DayNumber - DateOnly.FromDateTime(bill.BillDate).DayNumber;
                if (d <= 0) snapshot.APAging.Current += balBase;
                else if (d <= 30) snapshot.APAging.Days1To30 += balBase;
                else if (d <= 60) snapshot.APAging.Days31To60 += balBase;
                else snapshot.APAging.DaysOver60 += balBase;
            }

            snapshot.OverdueInvoicesCount = unpaidInvoices
                .Count(i => (today.DayNumber - i.Date.DayNumber) > 30);

            // -------------------------------------------------------------
            // THE FIX: ASSIGN THE DASHBOARD TOTALS DIRECTLY FROM THE BUCKETS
            // -------------------------------------------------------------
            snapshot.AROutstandingTotal = snapshot.ARAging.Current + snapshot.ARAging.Days1To30 + snapshot.ARAging.Days31To60 + snapshot.ARAging.DaysOver60;
            snapshot.APOutstandingTotal = snapshot.APAging.Current + snapshot.APAging.Days1To30 + snapshot.APAging.Days31To60 + snapshot.APAging.DaysOver60;

            snapshot.CurrentRatio = totLiabs != 0 ? totAssets / totLiabs : 0;
            snapshot.QuickRatio = totLiabs != 0 ? (cashBal + snapshot.AROutstandingTotal) / totLiabs : 0;
            snapshot.DaysSalesOutstanding = revMtd != 0 ? (snapshot.AROutstandingTotal / revMtd) * 30 : 0;

            // ═══════════════════════════════════════════════════════════════════
            // 8. TOP CUSTOMERS MTD
            // ═══════════════════════════════════════════════════════════════════
            var mtdInvoices = await ctx.SalesOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId &&
                            (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced) &&
                            o.Date >= DateOnly.FromDateTime(startOfMonth))
                .ToListAsync();

            snapshot.TopCustomers = mtdInvoices
                .GroupBy(o => o.Customer?.Name ?? "Unknown")
                .Select(g => new TopCustomerSummary
                {
                    CustomerName = g.Key,
                    RevenueMTD = g.Sum(o =>
                    {
                        decimal sub = o.Lines.Sum(l => l.Quantity * l.UnitPrice);
                        decimal disc = o.DiscountPercentage > 0 ? sub * (o.DiscountPercentage / 100) : o.DiscountAmount;
                        return (sub - disc) * (o.ExchangeRate > 0 ? o.ExchangeRate : 1);
                    }),
                    InvoiceCount = g.Count()
                })
                .OrderByDescending(c => c.RevenueMTD)
                .Take(5)
                .ToList();

            // ═══════════════════════════════════════════════════════════════════
            // 9. ACTION CENTER DRILL-DOWN DATA
            // ═══════════════════════════════════════════════════════════════════
            var unshippedOrders = await ctx.SalesOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Where(o => o.CompanyId == companyId && o.OrderNumber.StartsWith("INV"))
                .Where(o => o.Lines.Any(l => l.Item != null && !l.Item.IsService && l.QtyShipped < l.Quantity))
                .OrderBy(o => o.Date)
                .Take(20)
                .ToListAsync();

            snapshot.UnshippedOrderDetails = unshippedOrders.Select(o => new UnshippedOrderSummary
            {
                OrderId = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerName = o.Customer?.Name ?? "Unknown",
                Date = o.Date,
                OrderValue = o.Lines.Sum(l => l.Quantity * l.UnitPrice),
                DaysOld = today.DayNumber - o.Date.DayNumber
            }).ToList();

            var apExcBills = await ctx.VendorBills
                .AsNoTracking()
                .Where(b => b.CompanyId == companyId && b.MatchStatus == BillMatchStatus.Variance)
                .Take(20)
                .ToListAsync();

            var excVendorIds = apExcBills.Select(b => b.VendorId).Distinct().ToList();
            var excVendors = await ctx.Vendors
                .Where(v => excVendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

            snapshot.APExceptionDetails = apExcBills.Select(b => new APExceptionSummary
            {
                BillReference = b.ExternalInvoiceNumber ?? "N/A",
                VendorName = excVendors.GetValueOrDefault(b.VendorId, "Unknown"),
                BillAmount = b.TotalAmount,
                VarianceReason = b.MatchVarianceReason ?? string.Empty
            }).ToList();

            snapshot.LowStockDetails = physicalItems
                .Where(i => stockLevels.GetValueOrDefault(i.Id, 0) <= i.ReorderLevel)
                .OrderBy(i => stockLevels.GetValueOrDefault(i.Id, 0) - i.ReorderLevel)
                .Take(20)
                .Select(i => new LowStockSummary
                {
                    ItemName = i.Name,
                    SKU = i.SKU ?? string.Empty,
                    CurrentQty = stockLevels.GetValueOrDefault(i.Id, 0),
                    ReorderLevel = i.ReorderLevel
                })
                .ToList();

            snapshot.LastRefresh = DateTime.Now;
            return snapshot;
        }
    }
}
