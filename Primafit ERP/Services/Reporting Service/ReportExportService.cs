using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using iText.Layout.Borders; // NEW for professional table borders
using ClosedXML.Excel;
using Primafit_ERP.Components.Models.Reporting;

namespace Primafit_ERP.Services
{
    public class ReportExportService
    {
        // ==========================================
        // 1. GENERATE PROFESSIONAL PDF 
        // ==========================================
        public byte[] GeneratePdf(StandardReportData data)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

            document.SetFont(fontNormal);

            // --- 1. FINANCIAL HEADER ---
            document.Add(new Paragraph((data.CompanyName ?? "COMPANY NAME").ToUpper())
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(14)
                .SetFont(fontBold)
                .SetMarginBottom(0));

            document.Add(new Paragraph(data.ReportName ?? "Financial Report")
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(12)
                .SetFont(fontBold)
                .SetMarginBottom(2));

            document.Add(new Paragraph($"Reporting Period: {data.ReportingPeriod ?? "N/A"}  |  Currency: {data.Currency ?? "Base"}")
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(10)
                .SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginBottom(15));

            // --- 2. DATA TABLE ---
            if (data.Headers != null && data.Headers.Any())
            {
                var table = new Table(UnitValue.CreatePercentArray(data.Headers.Count)).UseAllAvailableWidth();

                // Professional Headers (Top and Bottom borders, no vertical lines)
                foreach (var header in data.Headers)
                {
                    Cell cell = new Cell()
                        .Add(new Paragraph(header).SetFont(fontBold).SetFontSize(9))
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1f))
                        .SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1f))
                        .SetTextAlignment(TextAlignment.LEFT);

                    // Align headers containing amount/value to the right
                    if (header.Contains("Amount") || header.Contains("Balance") || header.Contains("Value") || header.Contains("Debit") || header.Contains("Credit"))
                        cell.SetTextAlignment(TextAlignment.RIGHT);

                    table.AddHeaderCell(cell);
                }

                // Add Rows
                if (data.Rows != null && data.Rows.Any())
                {
                    foreach (var row in data.Rows)
                    {
                        // Detect if this is a total/summary row
                        bool isSummary = row.Any(c => c != null && (c.ToUpper().Contains("TOTAL") || c.ToUpper().Contains("SUMMARY") || c.ToUpper().Contains("PROFIT")));

                        foreach (var cellData in row)
                        {
                            var p = new Paragraph(cellData ?? "").SetFontSize(9);
                            if (isSummary) p.SetFont(fontBold);

                            Cell cell = new Cell().Add(p).SetBorder(Border.NO_BORDER);

                            // Summary rows get a top line (subtotal) and bold text
                            if (isSummary)
                            {
                                cell.SetBorderTop(new SolidBorder(ColorConstants.BLACK, 0.5f));
                            }
                            else
                            {
                                // Subtle bottom border for standard rows
                                cell.SetBorderBottom(new SolidBorder(ColorConstants.LIGHT_GRAY, 0.3f));
                            }

                            // If it's a number, align right perfectly
                            if (decimal.TryParse(cellData?.Replace(",", ""), out _))
                            {
                                cell.SetTextAlignment(TextAlignment.RIGHT);
                            }

                            table.AddCell(cell);
                        }
                    }
                }
                else
                {
                    Cell emptyCell = new Cell(1, data.Headers.Count)
                        .Add(new Paragraph("No data available for the selected period.")
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetFont(fontItalic))
                        .SetBorder(Border.NO_BORDER);
                    table.AddCell(emptyCell);
                }

                document.Add(table);
            }

            // --- 3. FOOTER (Generated By) ---
            document.Add(new Paragraph($"\nGenerated By: {data.GeneratedBy ?? "System User"} on {data.DateGenerated:yyyy-MM-dd HH:mm}")
                .SetTextAlignment(TextAlignment.LEFT)
                .SetFontSize(8)
                .SetFont(fontItalic)
                .SetFontColor(ColorConstants.GRAY));

            document.Close();
            return stream.ToArray();
        }

        // ==========================================
        // 2. GENERATE PROFESSIONAL EXCEL 
        // ==========================================
        public byte[] GenerateExcel(StandardReportData data)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Report Data");

            int columnCount = data.Headers?.Count ?? 5;

            // --- 1. FINANCIAL HEADER ---
            worksheet.Cell(1, 1).Value = (data.CompanyName ?? "COMPANY NAME").ToUpper();
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 14;

            worksheet.Cell(2, 1).Value = data.ReportName ?? "Financial Report";
            worksheet.Cell(2, 1).Style.Font.Bold = true;
            worksheet.Cell(2, 1).Style.Font.FontSize = 12;

            worksheet.Cell(3, 1).Value = $"Reporting Period: {data.ReportingPeriod ?? "N/A"}";
            worksheet.Cell(4, 1).Value = $"Currency: {data.Currency ?? "Base"}";

            // Merge headers across the top
            for (int i = 1; i <= 4; i++) worksheet.Range(i, 1, i, columnCount).Merge();

            // --- 2. DATA TABLE ---
            int currentRow = 6;

            if (data.Headers != null && data.Headers.Any())
            {
                for (int i = 0; i < data.Headers.Count; i++)
                {
                    var cell = worksheet.Cell(currentRow, i + 1);
                    cell.Value = data.Headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    cell.Style.Fill.BackgroundColor = XLColor.White; // Clean white background
                }
                currentRow++;
            }

            if (data.Rows != null && data.Rows.Any())
            {
                foreach (var row in data.Rows)
                {
                    bool isSummary = row.Any(c => c != null && (c.ToUpper().Contains("TOTAL") || c.ToUpper().Contains("SUMMARY") || c.ToUpper().Contains("PROFIT")));

                    for (int i = 0; i < row.Count; i++)
                    {
                        var cell = worksheet.Cell(currentRow, i + 1);
                        string val = row[i] ?? "";

                        // Smart Number Formatting
                        if (decimal.TryParse(val.Replace(",", ""), out decimal numVal))
                        {
                            cell.Value = numVal;
                            cell.Style.NumberFormat.Format = "#,##0.00"; // Standard Accounting Format
                        }
                        else
                        {
                            cell.Value = val;
                        }

                        if (isSummary)
                        {
                            cell.Style.Font.Bold = true;
                            cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                        }
                    }
                    currentRow++;
                }
            }

            // --- 3. FOOTER ---
            currentRow++;
            worksheet.Cell(currentRow, 1).Value = $"Generated By: {data.GeneratedBy ?? "System"} on {data.DateGenerated:yyyy-MM-dd HH:mm}";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 9;

            worksheet.Columns().AdjustToContents();

            // Freeze the header panes for scrolling
            worksheet.SheetView.FreezeRows(6);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}