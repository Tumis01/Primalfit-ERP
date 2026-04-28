using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;

namespace Primafit_ERP.Services
{
    public class PayslipService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public PayslipService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
            // Required for QuestPDF community license
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // --- FETCH DATA ---
        public async Task<PayrollItem?> GetPayslipDataAsync(Guid payrollItemId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.PayrollItems
                .Include(i => i.Employee).ThenInclude(e => e!.JobRole).ThenInclude(j => j!.Department)
                .Include(i => i.Employee).ThenInclude(e => e!.Branch)
                .Include(i => i.PayrollRun)
                .Include(i => i.CustomEarnings)
                .Include(i => i.CustomDeductions)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == payrollItemId && i.PayrollRun!.CompanyId == companyId);
        }

        // --- 1. GENERATE HTML (For UI Preview) ---
        public string GenerateHtmlPayslip(PayrollItem item, string companyName)
        {
            if (item.Employee == null || item.PayrollRun == null) return "<p>Invalid Data</p>";

            var sb = new StringBuilder();
            sb.AppendLine("<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px; background: #fff;'>");

            // Header
            sb.AppendLine($"<div style='text-align: center; border-bottom: 2px solid #1e293b; padding-bottom: 15px; margin-bottom: 20px;'>");
            sb.AppendLine($"<h1 style='margin: 0; color: #0f172a; font-size: 24px; text-transform: uppercase;'>{companyName}</h1>");
            sb.AppendLine($"<h3 style='margin: 5px 0 0 0; color: #475569; font-weight: normal;'>Payslip for {item.PayrollRun.Period}</h3>");
            sb.AppendLine("</div>");

            // Employee Info
            sb.AppendLine("<table style='width: 100%; margin-bottom: 20px; font-size: 14px;'>");
            sb.AppendLine($"<tr><td style='padding: 4px 0;'><strong>Employee Name:</strong> {item.Employee.FirstName} {item.Employee.LastName}</td>");
            sb.AppendLine($"<td style='padding: 4px 0;'><strong>Bank:</strong> {item.Employee.BankName}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 4px 0;'><strong>Staff ID:</strong> {item.Employee.EmployeeCode}</td>");
            sb.AppendLine($"<td style='padding: 4px 0;'><strong>Account No:</strong> {item.Employee.AccountNumber}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 4px 0;'><strong>Department:</strong> {item.Employee.JobRole?.Department?.Name}</td>");
            sb.AppendLine($"<td style='padding: 4px 0;'><strong>PFA & PIN:</strong> {item.Employee.PensionFundAdministrator} - {item.Employee.RSAPin}</td></tr>");
            sb.AppendLine("</table>");

            // Earnings & Deductions Grid
            sb.AppendLine("<div style='display: flex; gap: 20px;'>");

            // Earnings Column
            sb.AppendLine("<div style='flex: 1; border: 1px solid #e2e8f0; border-radius: 4px;'>");
            sb.AppendLine("<div style='background: #f8fafc; padding: 8px 12px; font-weight: bold; border-bottom: 1px solid #e2e8f0;'>EARNINGS</div>");
            sb.AppendLine("<table style='width: 100%; font-size: 14px; border-collapse: collapse;'>");
            sb.AppendLine($"<tr><td style='padding: 8px 12px;'>Basic Salary & Allowances</td><td style='padding: 8px 12px; text-align: right;'>{item.GrossPay:N2}</td></tr>");
            foreach (var earn in item.CustomEarnings)
                sb.AppendLine($"<tr><td style='padding: 8px 12px;'>{earn.Description}</td><td style='padding: 8px 12px; text-align: right;'>{earn.Amount:N2}</td></tr>");
            sb.AppendLine($"<tr style='font-weight: bold; border-top: 1px solid #e2e8f0;'><td style='padding: 8px 12px;'>Total Gross</td><td style='padding: 8px 12px; text-align: right;'>{(item.GrossPay + item.OtherEarnings):N2}</td></tr>");
            sb.AppendLine("</table></div>");

            // Deductions Column
            sb.AppendLine("<div style='flex: 1; border: 1px solid #e2e8f0; border-radius: 4px;'>");
            sb.AppendLine("<div style='background: #f8fafc; padding: 8px 12px; font-weight: bold; border-bottom: 1px solid #e2e8f0;'>DEDUCTIONS</div>");
            sb.AppendLine("<table style='width: 100%; font-size: 14px; border-collapse: collapse;'>");
            sb.AppendLine($"<tr><td style='padding: 8px 12px;'>PAYE Tax</td><td style='padding: 8px 12px; text-align: right;'>{item.PAYETax:N2}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 8px 12px;'>Pension (Employee)</td><td style='padding: 8px 12px; text-align: right;'>{item.EmployeePension:N2}</td></tr>");
            foreach (var ded in item.CustomDeductions)
                sb.AppendLine($"<tr><td style='padding: 8px 12px;'>{ded.Description}</td><td style='padding: 8px 12px; text-align: right;'>{ded.Amount:N2}</td></tr>");
            sb.AppendLine($"<tr style='font-weight: bold; border-top: 1px solid #e2e8f0; color: #e11d48;'><td style='padding: 8px 12px;'>Total Deductions</td><td style='padding: 8px 12px; text-align: right;'>{(item.PAYETax + item.EmployeePension + item.OtherDeductions):N2}</td></tr>");
            sb.AppendLine("</table></div>");

            sb.AppendLine("</div>");

            // Net Pay
            sb.AppendLine($"<div style='margin-top: 20px; background: #0f172a; color: white; padding: 15px 20px; border-radius: 8px; display: flex; justify-content: space-between; align-items: center;'>");
            sb.AppendLine("<span style='font-size: 16px; font-weight: bold; text-transform: uppercase;'>Net Net Payable</span>");
            sb.AppendLine($"<span style='font-size: 24px; font-weight: bold; font-family: monospace;'>{item.NetPay:N2}</span>");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        // --- 2. GENERATE PDF (Downloadable) ---
        public byte[] GeneratePdfPayslip(PayrollItem item, string companyName)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Element(compose =>
                    {
                        compose.Column(column =>
                        {
                            column.Item().AlignCenter().Text(companyName).SemiBold().FontSize(20).FontColor(Colors.BlueGrey.Darken4);
                            column.Item().AlignCenter().Text($"Payslip for {item.PayrollRun?.Period}").FontSize(14).FontColor(Colors.Grey.Darken2);
                            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        });
                    });

                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(column =>
                    {
                        // Employee Details
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text($"Employee: {item.Employee?.FirstName} {item.Employee?.LastName}").SemiBold();
                                col.Item().Text($"Staff ID: {item.Employee?.EmployeeCode}");
                                col.Item().Text($"Department: {item.Employee?.JobRole?.Department?.Name}");
                            });
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text($"Bank: {item.Employee?.BankName}").SemiBold();
                                col.Item().Text($"Account No: {item.Employee?.AccountNumber}");
                                col.Item().Text($"PFA: {item.Employee?.PensionFundAdministrator} ({item.Employee?.RSAPin})");
                            });
                        });

                        column.Item().PaddingVertical(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Financial Grid
                        column.Item().Row(row =>
                        {
                            // Earnings
                            row.RelativeItem().PaddingRight(10).Column(col =>
                            {
                                col.Item().Background(Colors.Grey.Lighten4).Padding(5).Text("EARNINGS").SemiBold();
                                col.Item().Padding(5).Row(r => { r.RelativeItem().Text("Basic & Allowances"); r.RelativeItem().AlignRight().Text(item.GrossPay.ToString("N2")); });
                                foreach (var earn in item.CustomEarnings)
                                    col.Item().Padding(5).Row(r => { r.RelativeItem().Text(earn.Description); r.RelativeItem().AlignRight().Text(earn.Amount.ToString("N2")); });

                                col.Item().PaddingTop(5).BorderTop(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Row(r => { r.RelativeItem().Text("Total Gross").SemiBold(); r.RelativeItem().AlignRight().Text((item.GrossPay + item.OtherEarnings).ToString("N2")).SemiBold(); });
                            });

                            // Deductions
                            row.RelativeItem().PaddingLeft(10).Column(col =>
                            {
                                col.Item().Background(Colors.Grey.Lighten4).Padding(5).Text("DEDUCTIONS").SemiBold();
                                col.Item().Padding(5).Row(r => { r.RelativeItem().Text("PAYE Tax"); r.RelativeItem().AlignRight().Text(item.PAYETax.ToString("N2")); });
                                col.Item().Padding(5).Row(r => { r.RelativeItem().Text("Pension (Employee)"); r.RelativeItem().AlignRight().Text(item.EmployeePension.ToString("N2")); });
                                foreach (var ded in item.CustomDeductions)
                                    col.Item().Padding(5).Row(r => { r.RelativeItem().Text(ded.Description); r.RelativeItem().AlignRight().Text(ded.Amount.ToString("N2")); });

                                col.Item().PaddingTop(5).BorderTop(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Row(r => { r.RelativeItem().Text("Total Deductions").SemiBold().FontColor(Colors.Red.Medium); r.RelativeItem().AlignRight().Text((item.PAYETax + item.EmployeePension + item.OtherDeductions).ToString("N2")).SemiBold().FontColor(Colors.Red.Medium); });
                            });
                        });

                        // Net Pay
                        column.Item().PaddingTop(30).Background(Colors.BlueGrey.Darken4).Padding(15).Row(row =>
                        {
                            row.RelativeItem().Text("NET PAYABLE").FontSize(14).SemiBold().FontColor(Colors.White);
                            row.RelativeItem().AlignRight().Text(item.NetPay.ToString("N2")).FontSize(18).SemiBold().FontColor(Colors.White);
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Generated by Primafit ERP on ");
                        x.Span($"{DateTime.UtcNow:MMM dd, yyyy}");
                    });
                });
            });

            return document.GeneratePdf();
        }

        // --- 3. GENERATE EXCEL (Bank Upload File) ---
        public async Task<byte[]> ExportPayrollToExcelAsync(Guid payrollRunId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var run = await ctx.PayrollRuns
                .Include(r => r.PayrollItems).ThenInclude(i => i.Employee)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == payrollRunId && r.CompanyId == companyId);

            if (run == null) throw new Exception("Payroll Run not found.");

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add($"Bank_Upload_{run.Period}");

            // Headers
            ws.Cell(1, 1).Value = "S/N";
            ws.Cell(1, 2).Value = "Staff ID";
            ws.Cell(1, 3).Value = "Employee Name";
            ws.Cell(1, 4).Value = "Bank Name";
            ws.Cell(1, 5).Value = "Account Number";
            ws.Cell(1, 6).Value = "Net Pay";
            var headerRow = ws.Range("A1:F1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Data
            int row = 2;
            int sn = 1;
            foreach (var item in run.PayrollItems.Where(i => i.NetPay > 0).OrderBy(i => i.Employee!.FirstName))
            {
                ws.Cell(row, 1).Value = sn++;
                ws.Cell(row, 2).Value = item.Employee!.EmployeeCode;
                ws.Cell(row, 3).Value = $"{item.Employee.FirstName} {item.Employee.LastName}";
                ws.Cell(row, 4).Value = item.Employee.BankName;
                ws.Cell(row, 5).Value = $"'{item.Employee.AccountNumber}"; // Prefix with ' to force string, preserving leading zeros
                ws.Cell(row, 6).Value = item.NetPay;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}