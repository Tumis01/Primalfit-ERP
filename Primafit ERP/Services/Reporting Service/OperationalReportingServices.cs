using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class OperationalReportingService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public OperationalReportingService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // =========================================================
        // 1. INVENTORY VALUATION REPORT (WACC)
        // =========================================================
        public async Task<StandardReportData> GenerateInventoryValuationAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            // Only track physical goods (IsService == false)
            var inventoryData = await (from i in ctx.Items.AsNoTracking()
                                       join s in ctx.StockLedgers.AsNoTracking() on i.Id equals s.ItemId into stock
                                       from s in stock.DefaultIfEmpty() // Left join to include items with 0 stock
                                       where i.CompanyId == companyId && !i.IsService
                                       group s by new { i.SKU, i.Name, i.UoM, i.WeightedAverageCost } into g
                                       select new
                                       {
                                           SKU = g.Key.SKU,
                                           ItemName = g.Key.Name,
                                           UoM = g.Key.UoM,
                                           WACC = g.Key.WeightedAverageCost,
                                           // Sum the QuantityChanged, defaulting to 0 if null
                                           TotalQty = g.Sum(x => x == null ? 0 : x.QuantityChanged)
                                       })
                                       .OrderBy(x => x.ItemName)
                                       .ToListAsync();

            var report = new StandardReportData
            {
                ReportName = "Inventory Valuation Report (WACC)",
                ReportingPeriod = $"As of {DateTime.Today:MMM dd, yyyy}",
                Headers = new List<string> { "SKU", "Item Description", "UoM", "Qty on Hand", "Unit Cost (WACC)", "Total Value" }
            };

            decimal grandTotalValue = 0;

            foreach (var item in inventoryData)
            {
                if (item.TotalQty <= 0) continue; // Only show items actually in stock

                decimal totalValue = item.TotalQty * item.WACC;
                grandTotalValue += totalValue;

                report.Rows.Add(new List<string>
                {
                    item.SKU,
                    item.ItemName,
                    item.UoM,
                    item.TotalQty.ToString("N2"),
                    item.WACC.ToString("N4"), // Show 4 decimals for accurate WACC
                    totalValue.ToString("N2")
                });
            }

            report.Rows.Add(new List<string> { "", "", "", "", "GRAND TOTAL VALUATION", grandTotalValue.ToString("N2") });

            return report;
        }

        // =========================================================
        // 2. INTERNAL CONSUMPTION / PROJECT ISSUE LOG
        // =========================================================
        public async Task<StandardReportData> GenerateConsumptionLogAsync(Guid companyId, DateOnly startDate, DateOnly endDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            // Based on earlier logic, project issues use Type=Sale and Reference starting with "PRJ:"
            var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
            var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

            var consumptionQuery = await (from s in ctx.StockLedgers.AsNoTracking()
                                          join i in ctx.Items.AsNoTracking() on s.ItemId equals i.Id
                                          where s.CompanyId == companyId
                                                && s.Date >= startDateTime && s.Date <= endDateTime
                                                && s.Type == StockMovementType.Sale
                                                && s.Reference.StartsWith("PRJ:")
                                          orderby s.Date descending
                                          select new
                                          {
                                              s.Date,
                                              s.Reference,
                                              i.SKU,
                                              i.Name,
                                              // StockLedger records outbound as negative, so we use Math.Abs
                                              ConsumedQty = Math.Abs(s.QuantityChanged),
                                              s.CostAtTime
                                          }).ToListAsync();

            var report = new StandardReportData
            {
                ReportName = "Internal Consumption & Project Issue Log",
                ReportingPeriod = $"{startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}",
                Headers = new List<string> { "Date", "Project/Ref", "SKU", "Item Description", "Qty Consumed", "Value at Issue" }
            };

            decimal grandTotalConsumed = 0;

            foreach (var log in consumptionQuery)
            {
                decimal value = log.ConsumedQty * log.CostAtTime;
                grandTotalConsumed += value;

                report.Rows.Add(new List<string>
                {
                    log.Date.ToString("yyyy-MM-dd"),
                    log.Reference.Replace("PRJ: ", ""), // Clean up the prefix for presentation
                    log.SKU,
                    log.Name,
                    log.ConsumedQty.ToString("N2"),
                    value.ToString("N2")
                });
            }

            report.Rows.Add(new List<string> { "", "", "", "", "TOTAL CONSUMPTION", grandTotalConsumed.ToString("N2") });

            return report;
        }

        // =========================================================
        // 3. STOCK AGING (SLOW MOVING ITEMS)
        // =========================================================
        public async Task<StandardReportData> GenerateStockAgingReportAsync(Guid companyId, int daysWithoutMovementThreshold = 90)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            // Find the last movement date for all physical items
            var agingData = await (from i in ctx.Items.AsNoTracking()
                                   join s in ctx.StockLedgers.AsNoTracking() on i.Id equals s.ItemId into stock
                                   where i.CompanyId == companyId && !i.IsService
                                   let lastMoveDate = stock.Max(x => (DateTime?)x.Date) // Might be null if never moved
                                   let totalQty = stock.Sum(x => x.QuantityChanged)
                                   where totalQty > 0 // Only care about items we actually have in stock
                                   select new
                                   {
                                       i.SKU,
                                       i.Name,
                                       TotalQty = totalQty,
                                       i.WeightedAverageCost,
                                       LastMoveDate = lastMoveDate
                                   }).ToListAsync();

            var report = new StandardReportData
            {
                ReportName = "Stock Aging (Slow Moving Items)",
                ReportingPeriod = $"Items inactive for {daysWithoutMovementThreshold}+ days",
                Headers = new List<string> { "SKU", "Item Description", "Qty on Hand", "Last Movement Date", "Days Inactive", "Capital Tied Up" }
            };

            decimal totalCapitalTiedUp = 0;
            var today = DateTime.UtcNow;

            foreach (var item in agingData)
            {
                // Calculate days since last movement. If never moved, calculate from today (effectively infinite/unknown, but let's flag it high)
                int daysInactive = item.LastMoveDate.HasValue
                                   ? (today - item.LastMoveDate.Value).Days
                                   : 999;

                if (daysInactive >= daysWithoutMovementThreshold)
                {
                    decimal capitalTiedUp = item.TotalQty * item.WeightedAverageCost;
                    totalCapitalTiedUp += capitalTiedUp;

                    report.Rows.Add(new List<string>
                    {
                        item.SKU,
                        item.Name,
                        item.TotalQty.ToString("N2"),
                        item.LastMoveDate.HasValue ? item.LastMoveDate.Value.ToString("yyyy-MM-dd") : "Never",
                        daysInactive == 999 ? "N/A" : daysInactive.ToString(),
                        capitalTiedUp.ToString("N2")
                    });
                }
            }

            // Sort by capital tied up (descending) so highest liability is at the top
            report.Rows = report.Rows.OrderByDescending(r => decimal.Parse(r[5])).ToList();

            report.Rows.Add(new List<string> { "", "", "", "", "TOTAL DEAD CAPITAL", totalCapitalTiedUp.ToString("N2") });

            return report;
        }
    }
}