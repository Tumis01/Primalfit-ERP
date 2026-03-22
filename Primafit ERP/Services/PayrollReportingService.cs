using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PayrollReportingService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public PayrollReportingService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<PayeScheduleRow>> GetPayeScheduleAsync(Guid runId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var items = await ctx.PayrollItems
                .Include(i => i.Employee)
                .Where(i => i.PayrollRunId == runId && i.Employee!.CompanyId == companyId && i.PAYETax > 0)
                .AsNoTracking()
                .ToListAsync();

            return items.Select(i => new PayeScheduleRow
            {
                EmployeeName = $"{i.Employee!.FirstName} {i.Employee.LastName}",
                TaxId = "TIN-PENDING", // In reality, add TIN to Employee model
                GrossPay = i.GrossPay + i.OtherEarnings,
                PensionDeducted = i.EmployeePension,
                PayeDeducted = i.PAYETax
            }).OrderBy(x => x.EmployeeName).ToList();
        }

        public async Task<List<PensionScheduleRow>> GetPensionScheduleAsync(Guid runId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var items = await ctx.PayrollItems
                .Include(i => i.Employee)
                .Where(i => i.PayrollRunId == runId && i.Employee!.CompanyId == companyId && (i.EmployeePension > 0 || i.EmployerPension > 0))
                .AsNoTracking()
                .ToListAsync();

            return items.Select(i => new PensionScheduleRow
            {
                EmployeeName = $"{i.Employee!.FirstName} {i.Employee.LastName}",
                PFA = string.IsNullOrWhiteSpace(i.Employee.PensionFundAdministrator) ? "UNASSIGNED" : i.Employee.PensionFundAdministrator,
                RSAPin = string.IsNullOrWhiteSpace(i.Employee.RSAPin) ? "UNASSIGNED" : i.Employee.RSAPin,
                EmployeeContribution = i.EmployeePension,
                EmployerContribution = i.EmployerPension
            }).OrderBy(x => x.PFA).ThenBy(x => x.EmployeeName).ToList();
        }

        public async Task<List<PayrollSummaryRow>> GetPayrollSummaryAsync(Guid runId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var items = await ctx.PayrollItems
                .Include(i => i.Employee).ThenInclude(e => e!.Branch)
                .Where(i => i.PayrollRunId == runId && i.Employee!.CompanyId == companyId)
                .AsNoTracking()
                .ToListAsync();

            return items.GroupBy(i => i.Employee!.Branch!.Name)
                .Select(g => new PayrollSummaryRow
                {
                    BranchName = g.Key,
                    EmployeeCount = g.Count(),
                    TotalGross = g.Sum(i => i.GrossPay + i.OtherEarnings),
                    TotalNet = g.Sum(i => i.NetPay),
                    TotalPAYE = g.Sum(i => i.PAYETax),
                    TotalPension = g.Sum(i => i.EmployeePension + i.EmployerPension)
                }).OrderBy(x => x.BranchName).ToList();
        }

        // --- EXPORT TO EXCEL ---
        public byte[] ExportScheduleToExcel<T>(List<T> data, string sheetName)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add(sheetName);

            if (!data.Any()) return Array.Empty<byte>();

            // Auto-generate headers based on properties
            var properties = typeof(T).GetProperties();
            for (int i = 0; i < properties.Length; i++)
            {
                ws.Cell(1, i + 1).Value = properties[i].Name;
            }

            var headerRow = ws.Range(1, 1, 1, properties.Length);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Fill Data
            int row = 2;
            foreach (var item in data)
            {
                for (int i = 0; i < properties.Length; i++)
                {
                    var val = properties[i].GetValue(item);

                    if (val is decimal d)
                    {
                        ws.Cell(row, i + 1).Value = d;
                        ws.Cell(row, i + 1).Style.NumberFormat.Format = "#,##0.00";
                    }
                    else
                    {
                        ws.Cell(row, i + 1).Value = val?.ToString();
                    }
                }
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        // Add this inside PayrollReportingService.cs

        public async Task<List<PayrollVarianceRow>> GetPayrollVarianceAsync(Guid currentRunId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var currentRun = await ctx.PayrollRuns
                .Include(r => r.PayrollItems).ThenInclude(i => i.Employee)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == currentRunId && r.CompanyId == companyId);

            if (currentRun == null) return new List<PayrollVarianceRow>();

            // Find the immediately preceding run based on RunDate
            var previousRun = await ctx.PayrollRuns
                .Include(r => r.PayrollItems)
                .AsNoTracking()
                .Where(r => r.CompanyId == companyId && r.RunDate < currentRun.RunDate)
                .OrderByDescending(r => r.RunDate)
                .FirstOrDefaultAsync();

            var varianceList = new List<PayrollVarianceRow>();

            foreach (var currentItem in currentRun.PayrollItems)
            {
                var prevItem = previousRun?.PayrollItems.FirstOrDefault(i => i.EmployeeId == currentItem.EmployeeId);

                varianceList.Add(new PayrollVarianceRow
                {
                    EmployeeName = $"{currentItem.Employee!.FirstName} {currentItem.Employee.LastName}",
                    CurrentGross = currentItem.GrossPay + currentItem.OtherEarnings,
                    CurrentNet = currentItem.NetPay,
                    PreviousGross = prevItem != null ? (prevItem.GrossPay + prevItem.OtherEarnings) : 0,
                    PreviousNet = prevItem != null ? prevItem.NetPay : 0
                });
            }

            // Only return rows where there is an actual financial variance
            return varianceList
                .Where(v => v.VarianceGross != 0 || v.VarianceNet != 0)
                .OrderBy(v => v.EmployeeName)
                .ToList();
        }

        public async Task<List<YtdTaxReportRow>> GetAnnualYtdTaxReportAsync(int year, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // Fetch all items for the given year from APPROVED or PAID runs
            var items = await ctx.PayrollItems
                .Include(i => i.Employee)
                .Include(i => i.PayrollRun)
                .Where(i => i.PayrollRun!.CompanyId == companyId
                         && i.PayrollRun.RunDate.Year == year
                         && i.PayrollRun.Status != PayrollRunStatus.Draft)
                .AsNoTracking()
                .ToListAsync();

            // Group by Employee to get annual totals
            return items.GroupBy(i => i.EmployeeId)
                .Select(g => new YtdTaxReportRow
                {
                    EmployeeName = $"{g.First().Employee!.FirstName} {g.First().Employee!.LastName}",
                    TaxId = "TIN-PENDING",
                    MonthsWorked = g.Count(),
                    TotalGross = g.Sum(i => i.GrossPay + i.OtherEarnings),
                    TotalPension = g.Sum(i => i.EmployeePension), // Employee portion for H1
                    TotalPAYE = g.Sum(i => i.PAYETax),
                    TotalNet = g.Sum(i => i.NetPay)
                })
                .OrderBy(x => x.EmployeeName)
                .ToList();
        }
    }
}