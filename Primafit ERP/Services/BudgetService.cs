using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class BudgetService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public BudgetService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }
        // Add these to BudgetService.cs
        public async Task<List<BudgetHeader>> GetAllBudgetsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BudgetHeaders
                .AsNoTracking()
                .Include(b => b.Lines)
                .Include(b => b.TransferLines)
                .Where(b => b.CompanyId == companyId)
                .OrderByDescending(b => b.IsActive).ThenBy(b => b.BudgetName)
                .ToListAsync();
        }

        public async Task<BudgetHeader?> GetBudgetByIdAsync(Guid id, Guid? companyId = null)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BudgetHeaders
                .Include(b => b.Lines)
                    .ThenInclude(l => l.PeriodAllocations)
                .Include(b => b.TransferLines) // NEW: Load Transfer Rules
                    .ThenInclude(t => t.PeriodAllocations)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && (!companyId.HasValue || b.CompanyId == companyId.Value));
        }

        public async Task<string> SaveBudgetAsync(BudgetHeader budget, string userId = "system")
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(budget.BudgetName)) return "Budget Name is required.";

            var existing = await ctx.BudgetHeaders
                .Include(b => b.Lines).ThenInclude(l => l.PeriodAllocations)
                .Include(b => b.TransferLines).ThenInclude(t => t.PeriodAllocations)
                .FirstOrDefaultAsync(b => b.Id == budget.Id);
            var previousSummary = existing == null
                ? "No previous version"
                : $"Lines={existing.Lines.Count}, Budget={existing.Lines.Sum(l => l.LimitAmount):N4}, Forecast={existing.Lines.Sum(l => l.PeriodAllocations.Sum(p => p.ForecastAmount ?? 0)):N4}, Active={existing.IsActive}";

            if (existing == null)
            {
                if (budget.Id == Guid.Empty) budget.Id = Guid.NewGuid();

                foreach (var line in budget.Lines)
                {
                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                    line.BudgetHeaderId = budget.Id;
                    NormalizeBudgetAllocations(line);
                    line.LimitAmount = line.PeriodAllocations.Sum(p => p.Amount ?? 0);

                    foreach (var period in line.PeriodAllocations)
                    {
                        if (period.Id == Guid.Empty) period.Id = Guid.NewGuid();
                        period.BudgetLineId = line.Id;
                    }
                }

                foreach (var tLine in budget.TransferLines)
                {
                    if (tLine.Id == Guid.Empty) tLine.Id = Guid.NewGuid();
                    tLine.BudgetHeaderId = budget.Id;
                    NormalizeTransferAllocations(tLine);
                    tLine.LimitAmount = tLine.PeriodAllocations.Sum(p => p.Amount ?? 0);

                    foreach (var period in tLine.PeriodAllocations)
                    {
                        if (period.Id == Guid.Empty) period.Id = Guid.NewGuid();
                        period.BudgetTransferLineId = tLine.Id;
                    }
                }
                ctx.BudgetHeaders.Add(budget);
            }

            else
            {
                budget.CompanyId = existing.CompanyId;
                ctx.Entry(existing).CurrentValues.SetValues(budget);

                // The editor sends nullable decimal fields. If a user opens a
                // budget and saves without changing it, a null sent by the form
                // must not erase an amount already stored in the database.
                RestoreExistingBudgetAllocations(budget, existing);

                ctx.BudgetLines.RemoveRange(existing.Lines);
                foreach (var line in budget.Lines)
                {
                    line.Id = Guid.NewGuid();
                    line.BudgetHeaderId = existing.Id;
                    NormalizeBudgetAllocations(line);
                    line.LimitAmount = line.PeriodAllocations.Sum(p => p.Amount ?? 0);
                    ctx.BudgetLines.Add(line);

                    foreach (var period in line.PeriodAllocations)
                    {
                        period.Id = Guid.NewGuid();
                        period.BudgetLineId = line.Id;
                        ctx.Set<BudgetPeriodAllocation>().Add(period);
                    }
                }

                ctx.Set<BudgetTransferLine>().RemoveRange(existing.TransferLines);
                foreach (var tLine in budget.TransferLines)
                {
                    tLine.Id = Guid.NewGuid();
                    tLine.BudgetHeaderId = existing.Id;
                    NormalizeTransferAllocations(tLine);
                    tLine.LimitAmount = tLine.PeriodAllocations.Sum(p => p.Amount ?? 0);
                    ctx.Set<BudgetTransferLine>().Add(tLine);

                    foreach (var period in tLine.PeriodAllocations)
                    {
                        period.Id = Guid.NewGuid();
                        period.BudgetTransferLineId = tLine.Id;
                        ctx.Set<BudgetTransferPeriodAllocation>().Add(period);
                    }
                }
            }

            ctx.AuditLogs.Add(new AuditLog
            {
                CompanyId = budget.CompanyId,
                UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId,
                Action = existing == null ? "BudgetCreated" : "BudgetUpdated",
                EntityType = nameof(BudgetHeader),
                EntityId = budget.Id,
                Details = $"Budget '{budget.BudgetName}' saved. Previous: {previousSummary}. New: Lines={budget.Lines.Count}, Budget={budget.Lines.Sum(l => l.LimitAmount):N4}, Forecast={budget.Lines.Sum(l => l.PeriodAllocations.Sum(p => p.ForecastAmount ?? 0)):N4}, Active={budget.IsActive}."
            });

            try
            {
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Error saving budget: {ex.Message}";
            }
        }

        private static void RestoreExistingBudgetAllocations(BudgetHeader incoming, BudgetHeader existing)
        {
            foreach (var line in incoming.Lines ?? new List<BudgetLine>())
            {
                var previousLine = existing.Lines.FirstOrDefault(x =>
                    x.Id == line.Id || x.GlAccountId == line.GlAccountId);

                foreach (var allocation in line.PeriodAllocations ?? new List<BudgetPeriodAllocation>())
                {
                    var previousAllocation = previousLine?.PeriodAllocations
                        .FirstOrDefault(x => x.AccountingPeriodId == allocation.AccountingPeriodId);

                    if (!allocation.Amount.HasValue && previousAllocation?.Amount.HasValue == true)
                        allocation.Amount = previousAllocation.Amount;

                    if (!allocation.ForecastAmount.HasValue && previousAllocation?.ForecastAmount.HasValue == true)
                        allocation.ForecastAmount = previousAllocation.ForecastAmount;
                }
            }

            foreach (var line in incoming.TransferLines ?? new List<BudgetTransferLine>())
            {
                var previousLine = existing.TransferLines.FirstOrDefault(x =>
                    x.Id == line.Id ||
                    (x.FromGlAccountId == line.FromGlAccountId && x.ToGlAccountId == line.ToGlAccountId));

                foreach (var allocation in line.PeriodAllocations ?? new List<BudgetTransferPeriodAllocation>())
                {
                    var previousAllocation = previousLine?.PeriodAllocations
                        .FirstOrDefault(x => x.AccountingPeriodId == allocation.AccountingPeriodId);

                    if (!allocation.Amount.HasValue && previousAllocation?.Amount.HasValue == true)
                        allocation.Amount = previousAllocation.Amount;
                }
            }
        }

        private static void NormalizeBudgetAllocations(BudgetLine line)
        {
            line.PeriodAllocations ??= new List<BudgetPeriodAllocation>();
            foreach (var allocation in line.PeriodAllocations)
            {
                allocation.Amount ??= 0m;
                allocation.ForecastAmount ??= 0m;
            }
        }

        private static void NormalizeTransferAllocations(BudgetTransferLine line)
        {
            line.PeriodAllocations ??= new List<BudgetTransferPeriodAllocation>();
            foreach (var allocation in line.PeriodAllocations)
                allocation.Amount ??= 0m;
        }

        // 2. CHECK FUNDS (The Core Logic)
        public async Task<string> ValidateFundsAsync(Guid companyId, List<(Guid SegCoaId, decimal Amount)> requests)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Get the Active Budget
            var activeBudget = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);

            // If no active budget exists, we assume spending is unrestricted.
            if (activeBudget == null) return string.Empty;

            foreach (var req in requests)
            {
                // 2. Identify the Budget Limit for this Account
                var budgetLine = activeBudget.Lines.FirstOrDefault(l => l.GlAccountId == req.SegCoaId);

                // If this specific account isn't budgeted (limit 0), we skip the check (allow spending).
                if (budgetLine == null || budgetLine.LimitAmount == 0) continue;

                // -------------------------------------------------------------
                // A. CALCULATE ACTUALS (Money already gone)
                // Sum of posted GL Transactions (Debits - Credits)
                // -------------------------------------------------------------
                decimal actualSpent = await ctx.GLTransactions
                    .Where(t => t.CompanyId == companyId && t.SegCoaId == req.SegCoaId)
                    .SumAsync(t => t.Debit - t.Credit);

                // -------------------------------------------------------------
                // B. CALCULATE ENCUMBRANCES (Money promised in POs)
                // We must find Open PO Lines that target this GL Account.
                // Since PO Lines link to Items, we join via the Item definition.
                // -------------------------------------------------------------

                // Fetch Item IDs that map to this GL Account (Inventory Asset or Expense Account)
                var relevantItemIds = await ctx.Items
                    .Where(i => i.CompanyId == companyId &&
                               (i.InventoryAssetAccountId == req.SegCoaId || i.CostOfGoodsSoldAccountId == req.SegCoaId))
                    .Select(i => i.Id)
                    .ToListAsync();

                decimal encumberedAmount = 0;

                if (relevantItemIds.Any())
                {
                    encumberedAmount = await ctx.PurchaseOrders
                        .Where(p => p.CompanyId == companyId
                                    && (p.Status == PurchaseOrderStatus.Open || p.Status == PurchaseOrderStatus.PartiallyReceived)
                                    && !p.IsInvoicePosted)
                        .SelectMany(p => p.Lines)
                        .Where(l => relevantItemIds.Contains(l.ItemId))
                        .SumAsync(l => l.QuantityOrdered * l.UnitCost);
                }

                // -------------------------------------------------------------
                // C. THE GATEKEEPER FORMULA
                // -------------------------------------------------------------
                decimal totalCommitted = actualSpent + encumberedAmount;
                decimal fundsAvailable = budgetLine.LimitAmount - totalCommitted;

                // Check if the NEW request fits
                if (req.Amount > fundsAvailable)
                {
                    // HARD STOP MESSAGE
                    return $@"BUDGET EXCEEDED for Account. 
                              Budget: {budgetLine.LimitAmount:N2} 
                              | Spent: {actualSpent:N2} 
                              | Committed(POs): {encumberedAmount:N2} 
                              | Available: {fundsAvailable:N2} 
                              | Requested: {req.Amount:N2}";
                }
            }

            return string.Empty; // All checks passed
        }
        
        public async Task<BudgetHeader?> GetActiveBudgetAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .AsNoTracking() 
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);
        }
        public async Task<string> DeleteBudgetAsync(Guid budgetId, Guid companyId, string userId = "system")
        {
            try
            {
                using var context = await _dbFactory.CreateDbContextAsync();

                // Fetch the budget along with its child lines so they are deleted cleanly
                var budget = await context.BudgetHeaders
                    .Include(b => b.Lines)
                        .ThenInclude(l => l.PeriodAllocations)
                    .Include(b => b.TransferLines)
                        .ThenInclude(t => t.PeriodAllocations)
                    .FirstOrDefaultAsync(b => b.Id == budgetId && b.CompanyId == companyId);

                if (budget == null)
                    return "Budget not found.";

                context.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyId,
                    UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId,
                    Action = "BudgetDeleted",
                    EntityType = nameof(BudgetHeader),
                    EntityId = budget.Id,
                    Details = $"Budget '{budget.BudgetName}' deleted. Lines={budget.Lines.Count}, Budget={budget.Lines.Sum(l => l.LimitAmount):N4}."
                });
                context.BudgetHeaders.Remove(budget);
                await context.SaveChangesAsync();

                return string.Empty; // Success
            }
            catch (Exception ex)
            {
                return $"An error occurred while deleting the budget: {ex.Message}";
            }
        }
        // ==========================================
        // PROPER PERIODIC BUDGET VARIANCE REPORT
        // ==========================================
        public async Task<StandardReportData> GeneratePeriodicVarianceReportAsync(Guid companyId, DateOnly start, DateOnly end, string searchAccount = "", Guid? budgetId = null)
        {
            ReportAccounting.ValidatePeriod(start, end);
            await using var db = await _dbFactory.CreateDbContextAsync();
            var budgets = await db.BudgetHeaders.AsNoTracking().Include(b => b.Lines).ThenInclude(l => l.PeriodAllocations)
                .Where(b => b.CompanyId == companyId && (budgetId.HasValue ? b.Id == budgetId.Value : b.IsActive)).ToListAsync();
            var report = new StandardReportData { ReportName = "Detailed Budget Variance Report",
                ReportingPeriod = $"{start:dd MMM yyyy} to {end:dd MMM yyyy} · Budgets/forecasts prorated by overlapping calendar days; PO commitments at period end",
                Headers = new() { "Account / Period", "Description", "Budget", "Forecast", "Actuals (Posted)", "Closing Encumbrance", "Total Committed", "Budget Variance", "Forecast Variance", "% Used" } };
            var lines = budgets.SelectMany(b => b.Lines).ToList();
            var accounts = await db.SegChartOfAccounts.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            var groups = lines.GroupBy(x => x.GlAccountId).Where(g => accounts.ContainsKey(g.Key)
                && (string.IsNullOrWhiteSpace(searchAccount) || (accounts[g.Key].AccountCode + " " + accounts[g.Key].Description).Contains(searchAccount, StringComparison.OrdinalIgnoreCase))).ToList();
            if (groups.Count == 0) { report.Rows.Add(new() { "N/A", "No budget accounts match these filters.", "", "", "", "", "", "", "", "" }); return report; }
            var debitTypes = (await db.Set<SegAccountType>().Where(t => t.IsDebit).Select(t => t.Id).ToListAsync()).ToHashSet();
            decimal Net(GLTransaction t) => debitTypes.Contains(accounts[t.SegCoaId].SegAccountTypeId) ? t.Debit - t.Credit : t.Credit - t.Debit;
            var accountIds = groups.Select(x => x.Key).ToList();
            var actuals = await ReportAccounting.PostedTransactions(db, companyId).Where(t => accountIds.Contains(t.SegCoaId) && t.PostingDate >= start && t.PostingDate <= end).ToListAsync();
            var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
            var to = end.ToDateTime(TimeOnly.MaxValue);
            var orders = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId && p.OrderDate <= to
                && p.Status != PurchaseOrderStatus.Request && p.Status != PurchaseOrderStatus.DraftInvoice && p.Status != PurchaseOrderStatus.Cancelled && p.Status != PurchaseOrderStatus.Closed
                && !p.IsDirectInvoice && p.ConvertedFromPONumber == null).ToListAsync();
            var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id);
            var receiptDates = await db.GoodsReceipts.AsNoTracking().Where(r => r.CompanyId == companyId && r.DateReceived <= to).ToDictionaryAsync(r => r.Id, r => r.DateReceived);
            var receipts = await ReportAccounting.PostedReceiptLines(db, companyId).Where(r => r.PurchaseOrderLineId.HasValue && receiptDates.Keys.Contains(r.GoodsReceiptId)).ToListAsync();
            decimal Encumbered(Guid accountId, DateOnly cutoff) => orders.Where(p => DateOnly.FromDateTime(p.OrderDate) <= cutoff).Sum(p => p.Lines.Where(l => items.TryGetValue(l.ItemId, out var item)
                && (item.IsService ? item.CostOfGoodsSoldAccountId : item.InventoryAssetAccountId) == accountId).Sum(l =>
                ReportAccounting.Money(Math.Max(0m, l.QuantityOrdered - receipts.Where(r => r.PurchaseOrderLineId == l.Id && DateOnly.FromDateTime(receiptDates[r.GoodsReceiptId]) <= cutoff).Sum(r => r.QuantityReceived)) * l.UnitCost * p.ExchangeRate)));
            decimal Allocation(IEnumerable<BudgetLine> group, DateOnly from, DateOnly until, bool forecast) => group.SelectMany(x => x.PeriodAllocations).Sum(a => {
                if (!periods.TryGetValue(a.AccountingPeriodId, out var p)) return 0m;
                var left = from > p.StartDate ? from : p.StartDate; var right = until < p.EndDate ? until : p.EndDate;
                return left > right ? 0m : (forecast ? a.ForecastAmount ?? 0m : a.Amount ?? 0m) * (right.DayNumber - left.DayNumber + 1m) / (p.EndDate.DayNumber - p.StartDate.DayNumber + 1m);
            });
            List<string> Row(string code, string description, decimal budget, decimal forecast, decimal actual, decimal encumbered) => new() {
                code, description, budget.ToString("N4"), forecast.ToString("N4"), actual.ToString("N4"), encumbered.ToString("N4"), (actual + encumbered).ToString("N4"),
                (budget - actual - encumbered).ToString("N4"), (forecast - actual - encumbered).ToString("N4"), (budget == 0 ? 0 : (actual + encumbered) / budget * 100).ToString("N2") + "%" };
            decimal totalBudget = 0, totalForecast = 0, totalActual = 0, totalEncumbered = 0;
            foreach (var group in groups.OrderBy(g => accounts[g.Key].AccountCode))
            {
                var budget = Allocation(group, start, end, false); var forecast = Allocation(group, start, end, true);
                var actual = actuals.Where(x => x.SegCoaId == group.Key).Sum(Net); var encumbered = Encumbered(group.Key, end);
                report.Rows.Add(Row(accounts[group.Key].AccountCode, accounts[group.Key].Description, budget, forecast, actual, encumbered));
                for (var month = new DateOnly(start.Year, start.Month, 1); month <= end; month = month.AddMonths(1))
                {
                    var monthEnd = month.AddMonths(1).AddDays(-1); var left = start > month ? start : month; var right = end < monthEnd ? end : monthEnd;
                    var monthActual = actuals.Where(x => x.SegCoaId == group.Key && x.PostingDate >= left && x.PostingDate <= right).Sum(Net);
                    report.Rows.Add(Row("", $"↳ {left:dd MMM} – {right:dd MMM yyyy}", Allocation(group, left, right, false), Allocation(group, left, right, true), monthActual, Encumbered(group.Key, right)));
                }
                totalBudget += budget; totalForecast += forecast; totalActual += actual; totalEncumbered += encumbered;
            }
            report.Rows.Add(Row("TOTALS", "COMPANY WIDE", totalBudget, totalForecast, totalActual, totalEncumbered));
            return report;
        }
    }
}
