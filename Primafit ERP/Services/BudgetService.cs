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
                .Include(b => b.Lines) // Include to count lines/sum total
                .Where(b => b.CompanyId == companyId)
                .OrderByDescending(b => b.IsActive).ThenBy(b => b.BudgetName)
                .ToListAsync();
        }

        public async Task<BudgetHeader?> GetBudgetByIdAsync(Guid id)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.BudgetHeaders
                .Include(b => b.Lines)
                    .ThenInclude(l => l.PeriodAllocations)
                .Include(b => b.TransferLines) // NEW: Load Transfer Rules
                    .ThenInclude(t => t.PeriodAllocations)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);
        }

        public async Task<string> SaveBudgetAsync(BudgetHeader budget)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(budget.BudgetName)) return "Budget Name is required.";

            var existing = await ctx.BudgetHeaders
                .Include(b => b.Lines).ThenInclude(l => l.PeriodAllocations)
                .Include(b => b.TransferLines).ThenInclude(t => t.PeriodAllocations)
                .FirstOrDefaultAsync(b => b.Id == budget.Id);

            if (existing == null)
            {
                if (budget.Id == Guid.Empty) budget.Id = Guid.NewGuid();

                foreach (var line in budget.Lines)
                {
                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
                    line.BudgetHeaderId = budget.Id;
                    // Use ?? 0 to safely sum nullable decimals
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
                    // Use ?? 0 to safely sum nullable decimals
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

                ctx.BudgetLines.RemoveRange(existing.Lines);
                foreach (var line in budget.Lines)
                {
                    line.Id = Guid.NewGuid();
                    line.BudgetHeaderId = existing.Id;
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

        public async Task<string> TransferBudgetAsync(Guid budgetId, Guid fromGlId, Guid toGlId, Guid periodId, decimal amount)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var budget = await ctx.BudgetHeaders
                    .Include(b => b.Lines).ThenInclude(l => l.PeriodAllocations)
                    .Include(b => b.TransferLines).ThenInclude(t => t.PeriodAllocations)
                    .FirstOrDefaultAsync(b => b.Id == budgetId);

                if (budget == null) return "Budget not found.";
                if (amount <= 0) return "Transfer amount must be greater than zero.";
                if (fromGlId == toGlId) return "Cannot transfer to the same account.";

                // 1. Check if the rule exists and permits this transfer
                var transferRule = budget.TransferLines.FirstOrDefault(t => t.FromGlAccountId == fromGlId && t.ToGlAccountId == toGlId);
                if (transferRule == null) return "No transfer rule exists permitting movement between these accounts.";

                var rulePeriod = transferRule.PeriodAllocations.FirstOrDefault(p => p.AccountingPeriodId == periodId);
                if (rulePeriod == null || rulePeriod.Amount == null || rulePeriod.Amount < amount)
                    return "Transfer exceeds the maximum allowed reappropriation limit for this period.";

                // 2. Locate the actual budget limits to modify
                var fromLine = budget.Lines.FirstOrDefault(l => l.GlAccountId == fromGlId);
                var toLine = budget.Lines.FirstOrDefault(l => l.GlAccountId == toGlId);

                if (fromLine == null) return "Source account is not in this budget.";
                if (toLine == null) return "Destination account is not in this budget.";

                var fromPeriod = fromLine.PeriodAllocations.FirstOrDefault(p => p.AccountingPeriodId == periodId);
                var toPeriod = toLine.PeriodAllocations.FirstOrDefault(p => p.AccountingPeriodId == periodId);

                // Treat null as 0 for balance checks
                decimal currentFromBalance = fromPeriod?.Amount ?? 0;
                decimal currentToBalance = toPeriod?.Amount ?? 0;

                if (fromPeriod == null || currentFromBalance < amount)
                    return "Insufficient funds in the source account for the selected period.";
                if (toPeriod == null)
                    return "Destination account does not have this period configured.";

                // 3. Execute the transfer on the actual budget limits
                fromPeriod.Amount = currentFromBalance - amount;
                toPeriod.Amount = currentToBalance + amount;

                // 4. Deduct from the Transfer Rule allowable limit so they can't infinitely transfer
                rulePeriod.Amount -= amount;

                // 5. Update Header Limits
                fromLine.LimitAmount = fromLine.PeriodAllocations.Sum(p => p.Amount ?? 0);
                toLine.LimitAmount = toLine.PeriodAllocations.Sum(p => p.Amount ?? 0);
                transferRule.LimitAmount = transferRule.PeriodAllocations.Sum(p => p.Amount ?? 0);

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Transfer failed: {ex.Message}";
            }
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
        public async Task<StandardReportData> GenerateVarianceReportAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var activeBudget = await ctx.BudgetHeaders
                .Include(b => b.Lines)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.IsActive);

            var report = new StandardReportData
            {
                ReportName = "Budget Variance & Commitment Report",
                ReportingPeriod = $"Active Budget: {activeBudget?.BudgetName ?? "None"}",
                Headers = new List<string> { "Account Code", "Account Name", "Limit (Budget)", "Actuals (Posted)", "Encumbered (POs)", "Total Committed", "Available Balance", "% Used" }
            };

            if (activeBudget == null || !activeBudget.Lines.Any())
            {
                report.Rows.Add(new List<string> { "N/A", "No Active Budget found.", "", "", "", "", "", "" });
                return report;
            }

            decimal grandBudget = 0, grandActuals = 0, grandEncumbered = 0, grandCommitted = 0, grandAvailable = 0;

            foreach (var line in activeBudget.Lines)
            {
                var account = await ctx.SegChartOfAccounts.FindAsync(line.GlAccountId);
                if (account == null) continue;

                // 1. Calculate Actuals (Posted GL)
                decimal actualSpent = await ctx.GLTransactions
                    .Where(t => t.CompanyId == companyId && t.SegCoaId == line.GlAccountId)
                    .SumAsync(t => t.Debit - t.Credit);

                // 2. Calculate Encumbrances (Open POs)
                var relevantItemIds = await ctx.Items
                    .Where(i => i.CompanyId == companyId &&
                                (i.InventoryAssetAccountId == line.GlAccountId || i.CostOfGoodsSoldAccountId == line.GlAccountId))
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

                // 3. Math
                decimal totalCommitted = actualSpent + encumberedAmount;
                decimal available = line.LimitAmount - totalCommitted;

                decimal percentUsed = 0;
                if (line.LimitAmount > 0) percentUsed = (totalCommitted / line.LimitAmount) * 100;

                // 4. Formatting
                report.Rows.Add(new List<string>
        {
            account.AccountCode,
            account.Description,
            line.LimitAmount.ToString("N2"),
            actualSpent.ToString("N2"),
            encumberedAmount.ToString("N2"),
            totalCommitted.ToString("N2"),
            available.ToString("N2"),
            $"{percentUsed:N1}%"
        });

                // Add to Grand Totals
                grandBudget += line.LimitAmount;
                grandActuals += actualSpent;
                grandEncumbered += encumberedAmount;
                grandCommitted += totalCommitted;
                grandAvailable += available;
            }

            decimal grandPercent = grandBudget > 0 ? (grandCommitted / grandBudget) * 100 : 0;

            report.Rows.Add(new List<string> { "", "", "", "", "", "", "", "" }); // Spacer
            report.Rows.Add(new List<string>
    {
        "TOTALS",
        "COMPANY WIDE",
        grandBudget.ToString("N2"),
        grandActuals.ToString("N2"),
        grandEncumbered.ToString("N2"),
        grandCommitted.ToString("N2"),
        grandAvailable.ToString("N2"),
        $"{grandPercent:N1}%"
    });

            return report;
        }
    }
}