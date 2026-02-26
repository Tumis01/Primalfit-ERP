using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using ClosedXML.Excel;
using Primafit_ERP.Components.Models.Reporting;
using System;
using System.IO;
using System.Linq;

namespace Primafit_ERP.Services
{
    public class ReportExportService
    {
        // ==========================================
        // 1. PDF GENERATOR
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
                // SMART COLUMN ALLOCATION
                float[] columnWidths = new float[data.Headers.Count];
                bool[] isNumericColumn = new bool[data.Headers.Count];

                for (int i = 0; i < data.Headers.Count; i++)
                {
                    string headerUpper = data.Headers[i].ToUpper();

                    // Right-align numbers and give them a standard width
                    if (headerUpper.Contains("AMOUNT") || headerUpper.Contains("BALANCE") ||
                        headerUpper.Contains("VALUE") || headerUpper.Contains("DEBIT") ||
                        headerUpper.Contains("CREDIT") || headerUpper.Contains("TOTAL") ||
                        headerUpper.Contains("QTY"))
                    {
                        isNumericColumn[i] = true;
                        columnWidths[i] = 2.5f;
                    }
                    // Left-align descriptions and make them wider
                    else if (headerUpper.Contains("NAME") || headerUpper.Contains("DESCRIPTION") ||
                             headerUpper.Contains("NARRATION") || headerUpper.Contains("ITEM") ||
                             headerUpper.Contains("ACCOUNT"))
                    {
                        isNumericColumn[i] = false;
                        columnWidths[i] = (headerUpper.Contains("CODE")) ? 2f : 5f; // Code needs less space than Name
                    }
                    // Default fallback
                    else
                    {
                        isNumericColumn[i] = false;
                        columnWidths[i] = 2f;
                    }
                }

                // Apply the dynamic widths
                var table = new Table(UnitValue.CreatePercentArray(columnWidths)).UseAllAvailableWidth();

                // Professional Headers
                for (int i = 0; i < data.Headers.Count; i++)
                {
                    Cell cell = new Cell()
                        .Add(new Paragraph(data.Headers[i]).SetFont(fontBold).SetFontSize(9))
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1f))
                        .SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1f))
                        .SetPaddingTop(4f)
                        .SetPaddingBottom(4f)
                        .SetTextAlignment(isNumericColumn[i] ? TextAlignment.RIGHT : TextAlignment.LEFT);

                    table.AddHeaderCell(cell);
                }

                // Add Rows
                if (data.Rows != null && data.Rows.Any())
                {
                    foreach (var row in data.Rows)
                    {
                        // PDF FIX: Only bold the row if Column 1 (Code) is EMPTY and Column 2 contains Summary Words
                        bool isSummary = false;
                        if (row.Count >= 2 && string.IsNullOrWhiteSpace(row[0]))
                        {
                            string col2 = (row[1] ?? "").ToUpper();
                            if (col2.Contains("TOTAL") || col2.Contains("SUMMARY") || col2.Contains("PROFIT"))
                            {
                                isSummary = true;
                            }
                        }

                        for (int i = 0; i < row.Count; i++)
                        {
                            string cellText = i < row.Count ? (row[i] ?? "") : "";
                            var p = new Paragraph(cellText).SetFontSize(9);

                            if (isSummary) p.SetFont(fontBold);

                            Cell cell = new Cell().Add(p)
                                .SetBorder(Border.NO_BORDER)
                                .SetPaddingTop(3f)
                                .SetPaddingBottom(3f);

                            if (isSummary)
                            {
                                cell.SetBorderTop(new SolidBorder(ColorConstants.BLACK, 0.5f));
                                cell.SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1.2f));
                            }
                            else
                            {
                                cell.SetBorderBottom(new SolidBorder(ColorConstants.LIGHT_GRAY, 0.3f));
                            }

                            // Use the header array logic to align dashes perfectly with numbers
                            cell.SetTextAlignment(isNumericColumn[i] ? TextAlignment.RIGHT : TextAlignment.LEFT);

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
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(10f);
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
        // 2. EXCEL GENERATOR
        // ==========================================
        public byte[] GenerateExcel(StandardReportData data)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Report Output");

            // --- 1. HEADER ---
            worksheet.Cell(1, 1).Value = (data.CompanyName ?? "COMPANY NAME").ToUpper();
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 14;

            worksheet.Cell(2, 1).Value = data.ReportName ?? "Financial Report";
            worksheet.Cell(2, 1).Style.Font.Bold = true;
            worksheet.Cell(2, 1).Style.Font.FontSize = 12;

            worksheet.Cell(3, 1).Value = $"Reporting Period: {data.ReportingPeriod ?? "N/A"}  |  Currency: {data.Currency ?? "Base"}";
            worksheet.Cell(3, 1).Style.Font.FontColor = XLColor.DarkGray;

            int currentRow = 5;

            // --- 2. DATA TABLE ---
            if (data.Headers != null && data.Headers.Any())
            {
                bool[] isNumericColumn = new bool[data.Headers.Count];

                // Write Headers
                for (int i = 0; i < data.Headers.Count; i++)
                {
                    string h = data.Headers[i].ToUpper();
                    isNumericColumn[i] = h.Contains("AMOUNT") || h.Contains("BALANCE") ||
                                         h.Contains("VALUE") || h.Contains("DEBIT") ||
                                         h.Contains("CREDIT") || h.Contains("TOTAL") ||
                                         h.Contains("QTY");

                    var cell = worksheet.Cell(currentRow, i + 1);
                    cell.Value = data.Headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

                    if (isNumericColumn[i])
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                currentRow++;

                // Write Rows
                if (data.Rows != null && data.Rows.Any())
                {
                    for (int r = 0; r < data.Rows.Count; r++)
                    {
                        var row = data.Rows[r];

                        // Summary Detection
                        bool isSummary = false;
                        if (row.Count >= 2 && string.IsNullOrWhiteSpace(row[0]))
                        {
                            string col2 = (row[1] ?? "").ToUpper();
                            if (col2.Contains("TOTAL") || col2.Contains("SUMMARY") || col2.Contains("PROFIT"))
                            {
                                isSummary = true;
                            }
                        }

                        for (int c = 0; c < row.Count; c++)
                        {
                            var cell = worksheet.Cell(currentRow, c + 1);
                            string cellText = c < row.Count ? (row[c] ?? "") : "";

                            // Logic to correctly process numbers vs text
                            if (isNumericColumn[c] && decimal.TryParse(cellText.Replace(",", ""), out decimal numericValue))
                            {
                                cell.Value = numericValue;
                                cell.Style.NumberFormat.Format = "#,##0.00"; // Enforce standard accounting decimal layout
                            }
                            else
                            {
                               
                                cell.Value = cellText;
                            }

                            // Style summary rows
                            if (isSummary)
                            {
                                cell.Style.Font.Bold = true;
                                cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                                cell.Style.Border.BottomBorder = XLBorderStyleValues.Medium; // Darker bottom line
                            }
                        }
                        currentRow++;
                    }
                }
                else
                {
                    worksheet.Cell(currentRow, 1).Value = "No data available for the selected period.";
                    worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
                    currentRow++;
                }

                // Auto-fit all columns nicely
                worksheet.Columns().AdjustToContents();
            }

            // --- 3. FOOTER ---
            currentRow++;
            worksheet.Cell(currentRow, 1).Value = $"Generated By: {data.GeneratedBy ?? "System User"} on {data.DateGenerated:yyyy-MM-dd HH:mm}";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;

            using var memStream = new MemoryStream();
            workbook.SaveAs(memStream);
            return memStream.ToArray();
        }
    }
}