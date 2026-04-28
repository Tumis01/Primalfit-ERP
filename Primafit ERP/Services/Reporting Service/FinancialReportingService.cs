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
    // DTO used strictly for Balance Sheet generation to avoid runtime errors with dynamic anonymous types
    public class BsTransactionDto
    {
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public int SegAccountTypeId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class FinancialReportingService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly SegCoaService _segCoaService;

        public FinancialReportingService(IDbContextFactory<AppDbContext> dbFactory, SegCoaService segCoaService)
        {
            _dbFactory = dbFactory;
            _segCoaService = segCoaService;
        }

        // =========================================================
        // HELPER: FORMAT NEGATIVE NUMBERS WITH PARENTHESES
        // =========================================================
        private string FormatCurrency(decimal amount)
        {
            return amount < 0 ? $"({Math.Abs(amount):N2})" : amount.ToString("N2");
        }

        // =========================================================
        // 1. TRIAL BALANCE
        // =========================================================
        public async Task<StandardReportData> GenerateTrialBalanceAsync(Guid companyId, DateOnly startDate, DateOnly endDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch & Group: Sum all debits and credits per account
            // NOTE: Joined with GLBatches to explicitly exclude Draft/Unposted transactions (Status == 2 is typically 'Posted')
            var query = await (from t in ctx.GLTransactions.AsNoTracking()
                               join a in ctx.SegChartOfAccounts.AsNoTracking() on t.SegCoaId equals a.Id
                               join b in ctx.GLBatches.AsNoTracking() on t.BatchId equals b.Id
                               where t.CompanyId == companyId
                                  && t.PostingDate >= startDate
                                  && t.PostingDate <= endDate
                                  && (int)b.Status == 2 // Enforcing Posted Only
                               group t by new { t.SegCoaId, a.AccountCode, a.Description } into g
                               orderby g.Key.AccountCode
                               select new
                               {
                                   AccountCode = g.Key.AccountCode,
                                   AccountName = g.Key.Description,
                                   TotalDebit = g.Sum(x => x.Debit),
                                   TotalCredit = g.Sum(x => x.Credit)
                               }).ToListAsync();

            var report = new StandardReportData
            {
                ReportName = "Trial Balance",
                ReportingPeriod = $"{startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}",
                // Removed "Net Balance" header, keeping it strictly Debit & Credit
                Headers = new List<string> { "Account Code", "Account Name", "Debit", "Credit" }
            };

            decimal grandDebit = 0;
            decimal grandCredit = 0;

            foreach (var row in query)
            {
                // 2. Calculate the Net Difference
                decimal netBalance = row.TotalDebit - row.TotalCredit;

                // Skip accounts with absolutely zero net balance to keep the report clean
                if (netBalance == 0) continue;

                decimal finalDebit = 0;
                decimal finalCredit = 0;

                // 3. Mutually Exclusive Placement: Net > 0 goes to Debit, Net < 0 goes to Credit
                if (netBalance > 0)
                {
                    finalDebit = netBalance;
                    grandDebit += finalDebit;
                }
                else if (netBalance < 0)
                {
                    finalCredit = Math.Abs(netBalance); // Remove the negative sign
                    grandCredit += finalCredit;
                }

                // 4. Format Output: Use "-" to visually leave the inactive column empty
                report.Rows.Add(new List<string>
                {
                    row.AccountCode,
                    row.AccountName,
                    finalDebit > 0 ? FormatCurrency(finalDebit) : "-",
                    finalCredit > 0 ? FormatCurrency(finalCredit) : "-"
                });
            }

            // 5. Mathematical Proof Footer
            string status = Math.Round(grandDebit, 2) == Math.Round(grandCredit, 2) ? "BALANCED" : "UNBALANCED";

            report.Rows.Add(new List<string>
            {
                "",
                $"GRAND TOTAL ({status})",
                FormatCurrency(grandDebit),
                FormatCurrency(grandCredit)
            });

            return report;
        }

        // =========================================================
        // 2. PROFIT & LOSS (Income Statement)
        // =========================================================
        public async Task<StandardReportData> GeneratePnLAsync(Guid companyId, DateOnly startDate, DateOnly endDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var accountTypes = await _segCoaService.GetAccountTypesAsync();

            // Fetch all transactions in the period for P&L accounts (IsBalanceSheet == false)
            var pnlTxns = await (from t in ctx.GLTransactions.AsNoTracking()
                                 join a in ctx.SegChartOfAccounts.AsNoTracking() on t.SegCoaId equals a.Id
                                 where t.CompanyId == companyId && t.PostingDate >= startDate && t.PostingDate <= endDate
                                 select new { t.Debit, t.Credit, a.SegAccountTypeId, a.AccountCode, a.Description })
                                 .ToListAsync();

            var report = new StandardReportData
            {
                ReportName = "Profit & Loss Statement",
                ReportingPeriod = $"{startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}",
                Headers = new List<string> { "Type", "Account", "Amount" },
                Rows = new List<List<string>>()
            };

            decimal totalRevenue = 0, totalCogs = 0, totalExpenses = 0;

            // Group by Account Type (Revenue, COGS, Expenses)
            foreach (var type in accountTypes.Where(t => !t.IsBalanceSheet))
            {
                var typeTxns = pnlTxns.Where(t => t.SegAccountTypeId == type.Id).ToList();
                if (!typeTxns.Any()) continue;

                report.Rows.Add(new List<string> { type.Description.ToUpper(), "", "" }); // Section Header

                decimal sectionTotal = 0;

                // Group by actual GL Account
                var groupedAccts = typeTxns.GroupBy(x => new { x.AccountCode, x.Description });
                foreach (var acct in groupedAccts)
                {
                    decimal debit = acct.Sum(x => x.Debit);
                    decimal credit = acct.Sum(x => x.Credit);

                    // Logic based on normal balance
                    decimal balance = type.IsDebit ? (debit - credit) : (credit - debit);
                    sectionTotal += balance;

                    report.Rows.Add(new List<string> { "", $"{acct.Key.AccountCode} - {acct.Key.Description}", FormatCurrency(balance) });
                }

                report.Rows.Add(new List<string> { "", $"Total {type.Description}", FormatCurrency(sectionTotal) });
                report.Rows.Add(new List<string> { "", "", "" }); // Spacer

                // Categorize for Gross/Net Profit math based on your Seed Data IDs
                if (type.Id == 9) totalRevenue += sectionTotal; // ID 9 = Revenue
                else if (type.Id == 10) totalCogs += sectionTotal; // ID 10 = Cost of Sales
                else totalExpenses += sectionTotal; // Everything else (Expenses, Tax, Finance Costs, etc)
            }

            // Summary Footer
            decimal grossProfit = totalRevenue - totalCogs;
            decimal netProfit = grossProfit - totalExpenses;

            report.Rows.Add(new List<string> { "", "GROSS PROFIT", FormatCurrency(grossProfit) });
            report.Rows.Add(new List<string> { "", "TOTAL EXPENSES", FormatCurrency(totalExpenses) });
            report.Rows.Add(new List<string> { "", "NET PROFIT (LOSS)", FormatCurrency(netProfit) });

            return report;
        }

        // =========================================================
        // 3. BALANCE SHEET
        // =========================================================
        public async Task<StandardReportData> GenerateBalanceSheetAsync(Guid companyId, DateOnly asOfDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var accountTypes = await _segCoaService.GetAccountTypesAsync();

            // Balance Sheet requires ALL transactions from the beginning of time up to AsOfDate
            var txns = await (from t in ctx.GLTransactions.AsNoTracking()
                              join a in ctx.SegChartOfAccounts.AsNoTracking() on t.SegCoaId equals a.Id
                              where t.CompanyId == companyId && t.PostingDate <= asOfDate
                              select new BsTransactionDto
                              {
                                  Debit = t.Debit,
                                  Credit = t.Credit,
                                  SegAccountTypeId = a.SegAccountTypeId,
                                  AccountCode = a.AccountCode,
                                  Description = a.Description
                              }).ToListAsync();

            var report = new StandardReportData
            {
                ReportName = "Balance Sheet",
                ReportingPeriod = $"As of {asOfDate:MMM dd, yyyy}",
                Headers = new List<string> { "Classification", "Account", "Balance" },
                Rows = new List<List<string>>()
            };

            decimal totalAssets = 0, totalLiabilities = 0, totalEquity = 0;

            // 1. ASSETS
            report.Rows.Add(new List<string> { "ASSETS", "", "" });
            foreach (var type in accountTypes.Where(t => t.IsBalanceSheet && t.IsDebit))
            {
                totalAssets += ProcessBsSection(txns, type, report);
            }
            report.Rows.Add(new List<string> { "", "TOTAL ASSETS", FormatCurrency(totalAssets) });
            report.Rows.Add(new List<string> { "", "", "" });

            // 2. LIABILITIES
            report.Rows.Add(new List<string> { "LIABILITIES", "", "" });
            foreach (var type in accountTypes.Where(t => t.IsBalanceSheet && !t.IsDebit && !t.Description.Contains("Capital") && !t.Description.Contains("Earnings") && !t.Description.Contains("Reserves")))
            {
                totalLiabilities += ProcessBsSection(txns, type, report);
            }
            report.Rows.Add(new List<string> { "", "TOTAL LIABILITIES", FormatCurrency(totalLiabilities) });
            report.Rows.Add(new List<string> { "", "", "" });

            // 3. EQUITY
            report.Rows.Add(new List<string> { "EQUITY", "", "" });
            foreach (var type in accountTypes.Where(t => t.IsBalanceSheet && !t.IsDebit && (t.Description.Contains("Capital") || t.Description.Contains("Earnings") || t.Description.Contains("Reserves"))))
            {
                totalEquity += ProcessBsSection(txns, type, report);
            }

            // Calculate current period Net Income (Revenue - Expenses) and add to Equity
            var pnlTxns = txns.Where(txn => !accountTypes.First(at => at.Id == txn.SegAccountTypeId).IsBalanceSheet).ToList();
            decimal currentNetIncome = 0;
            foreach (var txn in pnlTxns)
            {
                var type = accountTypes.First(at => at.Id == txn.SegAccountTypeId);
                decimal amount = type.IsDebit ? (txn.Debit - txn.Credit) : (txn.Credit - txn.Debit);
                currentNetIncome += type.IsDebit ? -amount : amount; // Income increases Equity, Expense decreases Equity
            }

            report.Rows.Add(new List<string> { "", "Current Year Net Income", FormatCurrency(currentNetIncome) });
            totalEquity += currentNetIncome;

            report.Rows.Add(new List<string> { "", "TOTAL EQUITY", FormatCurrency(totalEquity) });
            report.Rows.Add(new List<string> { "", "", "" });

            // VALIDATION
            report.Rows.Add(new List<string> { "CHECK", "TOTAL LIABILITIES & EQUITY", FormatCurrency(totalLiabilities + totalEquity) });

            return report;
        }

        private decimal ProcessBsSection(List<BsTransactionDto> txns, SegAccountType type, StandardReportData report)
        {
            var typeTxns = txns.Where(t => t.SegAccountTypeId == type.Id).ToList();
            if (!typeTxns.Any()) return 0;

            decimal sectionTotal = 0;
            var grouped = typeTxns.GroupBy(x => new { x.AccountCode, x.Description });

            foreach (var acct in grouped)
            {
                decimal dr = acct.Sum(x => x.Debit);
                decimal cr = acct.Sum(x => x.Credit);
                decimal bal = type.IsDebit ? (dr - cr) : (cr - dr);

                if (bal != 0)
                {
                    report.Rows.Add(new List<string> { type.Description, $"{acct.Key.AccountCode} - {acct.Key.Description}", FormatCurrency(bal) });
                    sectionTotal += bal;
                }
            }
            return sectionTotal;
        }

        // =========================================================
        // 4. CASHBOOK BATCH REPORT
        // =========================================================
        public async Task<StandardReportData> GenerateCashbookReportAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var batch = await ctx.CashbookBatches
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) throw new Exception("Batch not found.");

            // FIX 1: Manually calculate the dynamically unmapped totals!
            decimal totalDebits = batch.Entries.Sum(e => e.Debit);
            decimal totalCredits = batch.Entries.Sum(e => e.Credit);
            decimal closingBalance = batch.OpeningBalance + totalDebits - totalCredits;

            // Fetch Bank Account Info
            var bankAcct = await ctx.SegChartOfAccounts.FindAsync(batch.BankSegCoaId);

            var report = new StandardReportData
            {
                ReportName = "Cashbook Batch Detail",
                ReportingPeriod = $"Batch Ref: {batch.BatchReference}",
                Headers = new List<string> { "Date", "Reference", "Description", "Money In (Dr)", "Money Out (Cr)", "Running Balance" },

                // FIX 2: Initialize the Rows list to prevent NullReferenceExceptions!
                Rows = new List<List<string>>()
            };

            report.Rows.Add(new List<string> { "INFO", $"Bank Account: {bankAcct?.AccountCode} - {bankAcct?.Description}", "", "", "", "" });
            report.Rows.Add(new List<string> { "", "OPENING BALANCE", "", "", "", FormatCurrency(batch.OpeningBalance) });

            decimal runningBal = batch.OpeningBalance;

            foreach (var entry in batch.Entries.OrderBy(e => e.TransactionDate))
            {
                runningBal = runningBal + entry.Debit - entry.Credit;
                report.Rows.Add(new List<string>
                {
                    entry.TransactionDate.ToString("yyyy-MM-dd"),
                    entry.Reference,
                    entry.Description,
                    entry.Debit > 0 ? FormatCurrency(entry.Debit) : "-",
                    entry.Credit > 0 ? FormatCurrency(entry.Credit) : "-",
                    FormatCurrency(runningBal)
                });
            }

            // Use the manually calculated variables here in the footer
            report.Rows.Add(new List<string> { "", "CLOSING BALANCE", FormatCurrency(totalDebits), FormatCurrency(totalCredits), "", FormatCurrency(closingBalance) });

            return report;
        }
        public async Task<StandardReportData> GenerateBankLedgerAsync(Guid companyId, Guid bankAccountId, DateOnly startDate, DateOnly endDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var bankAcct = await ctx.SegChartOfAccounts.FindAsync(bankAccountId);
            if (bankAcct == null) throw new Exception("Bank account not found.");

            // 1. Calculate TRUE Opening Balance (Sum of all posted txns BEFORE the start date)
            decimal openingBalance = await (from t in ctx.GLTransactions.AsNoTracking()
                                            join b in ctx.GLBatches.AsNoTracking() on t.BatchId equals b.Id
                                            where t.CompanyId == companyId
                                               && t.SegCoaId == bankAccountId
                                               && t.PostingDate < startDate
                                               && (int)b.Status == 2 // Posted Only
                                            select t.Debit - t.Credit).SumAsync();

            // 2. Fetch Transactions IN the period
            var periodTxns = await (from t in ctx.GLTransactions.AsNoTracking()
                                    join b in ctx.GLBatches.AsNoTracking() on t.BatchId equals b.Id
                                    where t.CompanyId == companyId
                                       && t.SegCoaId == bankAccountId
                                       && t.PostingDate >= startDate
                                       && t.PostingDate <= endDate
                                       && (int)b.Status == 2 // Posted Only
                                    orderby t.PostingDate, t.CreatedAt
                                    select new
                                    {
                                        t.PostingDate,
                                        b.Description,
                                        t.Narration,
                                        t.Debit,
                                        t.Credit
                                    }).ToListAsync();

            decimal totalDebits = periodTxns.Sum(t => t.Debit);
            decimal totalCredits = periodTxns.Sum(t => t.Credit);
            decimal closingBalance = openingBalance + totalDebits - totalCredits;

            var report = new StandardReportData
            {
                CompanyName = "Primafit Enterprise", // Replace with dynamic company name if available
                ReportName = "Cashbook / Bank Ledger",
                ReportingPeriod = $"{startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}",
                GeneratedBy = "System User", // Replace with actual user if passed in
                DateGenerated = DateTime.UtcNow,
                Headers = new List<string> { "Date", "Reference", "Narration", "Money In (Dr)", "Money Out (Cr)", "Running Balance" },
                Rows = new List<List<string>>()
            };

            report.Rows.Add(new List<string> { "INFO", $"Bank Account: {bankAcct.AccountCode} - {bankAcct.Description}", "", "", "", "" });
            report.Rows.Add(new List<string> { "", "OPENING BALANCE", "", "", "", FormatCurrency(openingBalance) });

            decimal runningBal = openingBalance;

            foreach (var txn in periodTxns)
            {
                runningBal = runningBal + txn.Debit - txn.Credit;
                report.Rows.Add(new List<string>
                {
                    txn.PostingDate.ToString("yyyy-MM-dd"),
                    txn.Description ?? "-",
                    txn.Narration ?? "No Description",
                    txn.Debit > 0 ? FormatCurrency(txn.Debit) : "-",
                    txn.Credit > 0 ? FormatCurrency(txn.Credit) : "-",
                    FormatCurrency(runningBal)
                });
            }

            report.Rows.Add(new List<string> { "", "CLOSING BALANCE", FormatCurrency(totalDebits), FormatCurrency(totalCredits), "", FormatCurrency(closingBalance) });

            return report;
        }
    }
}