using ClosedXML.Excel;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Primafit_ERP.Components.Models;
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

        // ==========================================
        // 3. ERP INVOICE GENERATOR (PDF)
        // ==========================================
        // 1. Add "string currencyCode" to the parameters
        public byte[] GenerateInvoicePdf(SalesOrder order, CompanyDetails company, string currencyCode)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            document.SetFont(fontNormal);
            document.SetFontSize(10);

            // --- HEADER: Company Info & Invoice Details ---
            var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();

            // Left Side: Company Info
            var companyInfo = new Cell().SetBorder(Border.NO_BORDER);
            companyInfo.Add(new Paragraph(company.CompanyName?.ToUpper() ?? "COMPANY NAME").SetFont(fontBold).SetFontSize(16).SetFontColor(ColorConstants.DARK_GRAY));
            companyInfo.Add(new Paragraph(company.PhysicalAddress ?? "Company Address\nCity, Country"));
            companyInfo.Add(new Paragraph($"Email: {company.CompanyEmail ?? "N/A"} | Reg No: {company.ComanyRegNumber ?? "N/A"}"));
            headerTable.AddCell(companyInfo);

            // Right Side: Document Details
            var docDetails = new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT);
            string docType = order.Status == OrderStatus.Quote ? "QUOTATION" : (order.OrderNumber.StartsWith("INV") ? "Sales INVOICE" : "SALES Invoice");

            docDetails.Add(new Paragraph(docType).SetFont(fontBold).SetFontSize(20).SetFontColor(ColorConstants.BLACK));
            docDetails.Add(new Paragraph($"Document #: {order.OrderNumber}").SetFont(fontBold));
            docDetails.Add(new Paragraph($"Date: {order.Date:dd MMM, yyyy}"));
            headerTable.AddCell(docDetails);

            document.Add(headerTable);
            document.Add(new Paragraph("\n")); // Spacer

            // --- BILL TO: Customer Info ---
            var billToTable = new Table(UnitValue.CreatePercentArray(new float[] { 1 })).UseAllAvailableWidth();
            var billToCell = new Cell().SetBorder(Border.NO_BORDER);
            billToCell.Add(new Paragraph("BILL TO:").SetFont(fontBold).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
            billToCell.Add(new Paragraph(order.Customer?.Name ?? "Unknown Customer").SetFont(fontBold).SetFontSize(12));
            billToCell.Add(new Paragraph(order.Customer?.Email ?? ""));
            billToCell.Add(new Paragraph(order.Customer?.Phone ?? ""));
            billToTable.AddCell(billToCell);

            document.Add(billToTable);
            document.Add(new Paragraph("\n")); // Spacer

            // --- LINE ITEMS TABLE ---
            var itemTable = new Table(UnitValue.CreatePercentArray(new float[] { 4, 1, 2, 2 })).UseAllAvailableWidth();

            // 2. Use the passed-in currencyCode instead of order.Currency
            string curr = string.IsNullOrEmpty(currencyCode) ? "" : currencyCode;

            string[] headers = { "Item Description", "Qty", $"Unit Price ({curr})", $"Total ({curr})" };
            foreach (var h in headers)
            {
                itemTable.AddHeaderCell(new Cell()
                    .Add(new Paragraph(h).SetFont(fontBold))
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetPadding(5)
                    .SetTextAlignment(h == "Item Description" ? TextAlignment.LEFT : TextAlignment.RIGHT));
            }

            // Table Rows
            decimal subTotal = 0;
            foreach (var line in order.Lines)
            {
                string itemName = line.Item?.Name ?? "Unknown Item";
                decimal lineTotal = line.Quantity * line.UnitPrice;
                subTotal += lineTotal;

                itemTable.AddCell(new Cell().Add(new Paragraph(itemName)).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.Quantity.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.UnitPrice.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(lineTotal.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
            }
            document.Add(itemTable);

            // --- SUMMARY TOTALS (Bottom Right) ---
            var totalsTable = new Table(UnitValue.CreatePercentArray(new float[] { 7, 3 })).UseAllAvailableWidth();

            // Calculate Math
            decimal discountValue = order.DiscountPercentage > 0 ? subTotal * (order.DiscountPercentage / 100) : order.DiscountAmount;
            decimal discountedSubTotal = subTotal - discountValue;
            decimal taxAmount = order.GrandTotalForeign - discountedSubTotal; // Safely derive tax from the saved GrandTotal

            void AddTotalRow(string label, decimal amount, bool isBold = false)
            {
                totalsTable.AddCell(new Cell().Add(new Paragraph(label)).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
                totalsTable.AddCell(new Cell().Add(new Paragraph(amount.ToString("N2"))).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
            }

            AddTotalRow("Subtotal:", subTotal);
            if (discountValue > 0) AddTotalRow("Discount:", -discountValue);
            if (taxAmount > 0) AddTotalRow("Tax / VAT:", taxAmount);

            // Grand Total Row with top border
            totalsTable.AddCell(new Cell().Add(new Paragraph($"Grand Total ({curr}):").SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));
            totalsTable.AddCell(new Cell().Add(new Paragraph(order.GrandTotalForeign.ToString("N2")).SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));

            document.Add(totalsTable);

            // --- FOOTER ---
            var fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);
            document.Add(new Paragraph("\n\nThank you for your business!")
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFont(fontItalic)
                .SetFontColor(ColorConstants.GRAY));

            document.Close();
            return stream.ToArray();
        }
        // ==========================================
        // 5. DIRECT AR INVOICE GENERATOR (PDF)
        // ==========================================
        public byte[] GenerateDirectInvoicePdf(SalesOrder order, CompanyDetails company)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

            document.SetFont(fontNormal).SetFontSize(10);

            // --- HEADER ---
            var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();

            var companyInfo = new Cell().SetBorder(Border.NO_BORDER);
            companyInfo.Add(new Paragraph(company.CompanyName?.ToUpper() ?? "COMPANY NAME").SetFont(fontBold).SetFontSize(16).SetFontColor(ColorConstants.DARK_GRAY));
            companyInfo.Add(new Paragraph(company.PhysicalAddress ?? "Company Address\nCity, Country"));
            companyInfo.Add(new Paragraph($"Email: {company.CompanyEmail ?? "N/A"} | Reg No: {company.ComanyRegNumber ?? "N/A"}"));
            headerTable.AddCell(companyInfo);

            var docDetails = new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT);
            docDetails.Add(new Paragraph("AR INVOICE").SetFont(fontBold).SetFontSize(20).SetFontColor(ColorConstants.BLACK));
            docDetails.Add(new Paragraph($"Invoice #: {order.OrderNumber}").SetFont(fontBold));
            docDetails.Add(new Paragraph($"Date: {order.Date:dd MMM, yyyy}"));
            headerTable.AddCell(docDetails);

            document.Add(headerTable);
            document.Add(new Paragraph("\n"));

            // --- BILL TO ---
            var billToTable = new Table(UnitValue.CreatePercentArray(new float[] { 1 })).UseAllAvailableWidth();
            var billToCell = new Cell().SetBorder(Border.NO_BORDER);
            billToCell.Add(new Paragraph("BILL TO:").SetFont(fontBold).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
            billToCell.Add(new Paragraph(order.Customer?.Name ?? "Unknown Customer").SetFont(fontBold).SetFontSize(12));
            if (!string.IsNullOrEmpty(order.Customer?.Email)) billToCell.Add(new Paragraph(order.Customer.Email));
            if (!string.IsNullOrEmpty(order.Customer?.Phone)) billToCell.Add(new Paragraph(order.Customer.Phone));
            billToTable.AddCell(billToCell);

            document.Add(billToTable);
            document.Add(new Paragraph("\n"));

            // --- LINE ITEMS ---
            var itemTable = new Table(UnitValue.CreatePercentArray(new float[] { 4, 1, 2, 2 })).UseAllAvailableWidth();

            string curr = order.Currency?.CurrencyCode ?? "";
            string[] headers = { "Description / Service Rendered", "Qty", $"Unit Price ({curr})", $"Total ({curr})" };
            foreach (var h in headers)
            {
                itemTable.AddHeaderCell(new Cell().Add(new Paragraph(h).SetFont(fontBold)).SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetPadding(5).SetTextAlignment(h.Contains("Description") ? TextAlignment.LEFT : TextAlignment.RIGHT));
            }

            decimal subTotal = 0;
            foreach (var line in order.Lines)
            {
                // CRITICAL DIFFERENCE: Uses Description instead of line.Item.Name
                string desc = string.IsNullOrWhiteSpace(line.Description) ? "Ad-hoc Service" : line.Description;
                decimal lineTotal = line.Quantity * line.UnitPrice;
                subTotal += lineTotal;

                itemTable.AddCell(new Cell().Add(new Paragraph(desc)).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.Quantity.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.UnitPrice.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(lineTotal.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
            }
            document.Add(itemTable);

            // --- TOTALS ---
            var totalsTable = new Table(UnitValue.CreatePercentArray(new float[] { 7, 3 })).UseAllAvailableWidth();
            decimal discountValue = order.DiscountPercentage > 0 ? subTotal * (order.DiscountPercentage / 100) : order.DiscountAmount;
            decimal discountedSubTotal = subTotal - discountValue;
            decimal taxAmount = order.GrandTotalForeign - discountedSubTotal;

            void AddTotalRow(string label, decimal amount, bool isBold = false)
            {
                totalsTable.AddCell(new Cell().Add(new Paragraph(label)).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
                totalsTable.AddCell(new Cell().Add(new Paragraph(amount.ToString("N2"))).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
            }

            AddTotalRow("Subtotal:", subTotal);
            if (discountValue > 0) AddTotalRow("Discount:", -discountValue);
            if (taxAmount > 0) AddTotalRow("Tax / VAT:", taxAmount);

            totalsTable.AddCell(new Cell().Add(new Paragraph($"Grand Total ({curr}):").SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));
            totalsTable.AddCell(new Cell().Add(new Paragraph(order.GrandTotalForeign.ToString("N2")).SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));

            document.Add(totalsTable);

            document.Add(new Paragraph("\n\nThank you for your business!")
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFont(fontItalic)
                .SetFontColor(ColorConstants.GRAY));

            document.Close();
            return stream.ToArray();
        }
        // ==========================================
        // 6. PURCHASE ORDER GENERATOR (PDF)
        // ==========================================
        public byte[] GeneratePurchaseOrderPdf(PurchaseOrder order, CompanyDetails company, string vendorName, string vendorEmail, Dictionary<Guid, string> itemNames)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

            document.SetFont(fontNormal).SetFontSize(10);

            // --- HEADER ---
            var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();

            var companyInfo = new Cell().SetBorder(Border.NO_BORDER);
            companyInfo.Add(new Paragraph(company.CompanyName?.ToUpper() ?? "COMPANY NAME").SetFont(fontBold).SetFontSize(16).SetFontColor(ColorConstants.DARK_GRAY));
            companyInfo.Add(new Paragraph(company.PhysicalAddress ?? "Company Address"));
            companyInfo.Add(new Paragraph($"Email: {company.CompanyEmail ?? "N/A"} | Reg No: {company.ComanyRegNumber ?? "N/A"}"));
            headerTable.AddCell(companyInfo);

            var docDetails = new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT);
            string docType = order.Status == PurchaseOrderStatus.Request ? "PURCHASE REQUEST" : "PURCHASE ORDER";

            docDetails.Add(new Paragraph(docType).SetFont(fontBold).SetFontSize(20).SetFontColor(ColorConstants.BLACK));
            docDetails.Add(new Paragraph($"PO Number: {order.OrderNumber}").SetFont(fontBold));
            docDetails.Add(new Paragraph($"Date: {order.OrderDate:dd MMM, yyyy}"));
            headerTable.AddCell(docDetails);

            document.Add(headerTable);
            document.Add(new Paragraph("\n"));

            // --- VENDOR INFO ---
            var vendorTable = new Table(UnitValue.CreatePercentArray(new float[] { 1 })).UseAllAvailableWidth();
            var vendorCell = new Cell().SetBorder(Border.NO_BORDER);
            vendorCell.Add(new Paragraph("VENDOR:").SetFont(fontBold).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
            vendorCell.Add(new Paragraph(vendorName).SetFont(fontBold).SetFontSize(12));
            if (!string.IsNullOrEmpty(vendorEmail)) vendorCell.Add(new Paragraph(vendorEmail));
            vendorTable.AddCell(vendorCell);

            document.Add(vendorTable);
            document.Add(new Paragraph("\n"));

            // --- LINE ITEMS ---
            var itemTable = new Table(UnitValue.CreatePercentArray(new float[] { 4, 1, 2, 2 })).UseAllAvailableWidth();

            string curr = order.Currency?.CurrencyCode ?? "";
            string[] headers = { "Item Description", "Qty", $"Unit Cost ({curr})", $"Total ({curr})" };
            foreach (var h in headers)
            {
                itemTable.AddHeaderCell(new Cell().Add(new Paragraph(h).SetFont(fontBold)).SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetPadding(5).SetTextAlignment(h == "Item Description" ? TextAlignment.LEFT : TextAlignment.RIGHT));
            }

            decimal subTotal = 0;
            foreach (var line in order.Lines)
            {
                // FIXED: Use the dictionary to look up the item name safely!
                string itemName = itemNames.ContainsKey(line.ItemId) ? itemNames[line.ItemId] : "Unknown Item";

                decimal lineTotal = line.LineTotal;
                subTotal += lineTotal;

                itemTable.AddCell(new Cell().Add(new Paragraph(itemName)).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.QuantityOrdered.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.UnitCost.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(lineTotal.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
            }
            document.Add(itemTable);

            // --- TOTALS ---
            var totalsTable = new Table(UnitValue.CreatePercentArray(new float[] { 7, 3 })).UseAllAvailableWidth();
            decimal discountValue = order.DiscountPercentage > 0 ? subTotal * (order.DiscountPercentage / 100) : order.DiscountAmount;
            decimal discountedSubTotal = subTotal - discountValue;
            decimal taxAmount = order.GrandTotalForeign - discountedSubTotal;

            void AddTotalRow(string label, decimal amount, bool isBold = false)
            {
                totalsTable.AddCell(new Cell().Add(new Paragraph(label)).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
                totalsTable.AddCell(new Cell().Add(new Paragraph(amount.ToString("N2"))).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
            }

            AddTotalRow("Subtotal:", subTotal);
            if (discountValue > 0) AddTotalRow("Discount:", -discountValue);
            if (taxAmount > 0) AddTotalRow("Tax / VAT:", taxAmount);

            totalsTable.AddCell(new Cell().Add(new Paragraph($"Grand Total ({curr}):").SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));
            totalsTable.AddCell(new Cell().Add(new Paragraph(order.GrandTotalForeign.ToString("N2")).SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));

            document.Add(totalsTable);
            document.Close();
            return stream.ToArray();
        }
        public byte[] GenerateVendorBillPdf(VendorBill bill, CompanyDetails company, string vendorName, string currencyCode)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

            document.SetFont(fontNormal).SetFontSize(10);

            // --- HEADER ---
            var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();

            var companyInfo = new Cell().SetBorder(Border.NO_BORDER);
            companyInfo.Add(new Paragraph(company.CompanyName?.ToUpper() ?? "COMPANY NAME").SetFont(fontBold).SetFontSize(16).SetFontColor(ColorConstants.DARK_GRAY));
            companyInfo.Add(new Paragraph(company.PhysicalAddress ?? "Company Address"));
            companyInfo.Add(new Paragraph($"Email: {company.CompanyEmail ?? "N/A"}"));
            headerTable.AddCell(companyInfo);

            var docDetails = new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT);
            string docType = bill.IsDirectBill ? "DIRECT VENDOR BILL" : "VENDOR BILL";

            docDetails.Add(new Paragraph(docType).SetFont(fontBold).SetFontSize(20).SetFontColor(ColorConstants.BLACK));
            docDetails.Add(new Paragraph($"Bill Ref #: {bill.ExternalInvoiceNumber}").SetFont(fontBold));
            docDetails.Add(new Paragraph($"Date: {bill.BillDate:dd MMM, yyyy}"));
            headerTable.AddCell(docDetails);

            document.Add(headerTable);
            document.Add(new Paragraph("\n"));

            // --- VENDOR INFO ---
            var vendorTable = new Table(UnitValue.CreatePercentArray(new float[] { 1 })).UseAllAvailableWidth();
            var vendorCell = new Cell().SetBorder(Border.NO_BORDER);
            vendorCell.Add(new Paragraph("VENDOR:").SetFont(fontBold).SetFontSize(10).SetFontColor(ColorConstants.GRAY));
            vendorCell.Add(new Paragraph(vendorName).SetFont(fontBold).SetFontSize(12));
            vendorTable.AddCell(vendorCell);

            document.Add(vendorTable);
            document.Add(new Paragraph("\n"));

            // --- LINE ITEMS ---
            var itemTable = new Table(UnitValue.CreatePercentArray(new float[] { 4, 1, 2, 2 })).UseAllAvailableWidth();

            string[] headers = { "Description", "Qty", $"Unit Cost ({currencyCode})", $"Total ({currencyCode})" };
            foreach (var h in headers)
            {
                itemTable.AddHeaderCell(new Cell().Add(new Paragraph(h).SetFont(fontBold)).SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetPadding(5).SetTextAlignment(h.Contains("Description") ? TextAlignment.LEFT : TextAlignment.RIGHT));
            }

            decimal subTotal = 0;
            foreach (var line in bill.Lines)
            {
                // FIX: Look at the line description directly, since it was moved from the header
                string lineDescription = string.IsNullOrWhiteSpace(line.Description) ? "Expense / Ad-Hoc Service" : line.Description;

                decimal lineTotal = line.LineTotal;
                subTotal += lineTotal;

                itemTable.AddCell(new Cell().Add(new Paragraph(lineDescription)).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.QuantityBilled.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(line.UnitCostBilled.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
                itemTable.AddCell(new Cell().Add(new Paragraph(lineTotal.ToString("N2"))).SetTextAlignment(TextAlignment.RIGHT).SetPadding(5));
            }
            document.Add(itemTable);

            // --- TOTALS AND TAX ---
            var totalsTable = new Table(UnitValue.CreatePercentArray(new float[] { 7, 3 })).UseAllAvailableWidth();

            // Since we added Tax fields to VendorBill, we must calculate the tax difference for the summary section
            decimal taxAmount = bill.TotalAmountForeign - subTotal;

            void AddTotalRow(string label, decimal amount, bool isBold = false)
            {
                totalsTable.AddCell(new Cell().Add(new Paragraph(label)).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
                totalsTable.AddCell(new Cell().Add(new Paragraph(amount.ToString("N2"))).SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT).SetPadding(3).SetFont(isBold ? fontBold : fontNormal));
            }

            AddTotalRow("Subtotal:", subTotal);
            if (taxAmount > 0) AddTotalRow("Tax / VAT:", taxAmount);

            totalsTable.AddCell(new Cell().Add(new Paragraph($"Grand Total ({currencyCode}):").SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));
            totalsTable.AddCell(new Cell().Add(new Paragraph(bill.TotalAmountForeign.ToString("N2")).SetFont(fontBold).SetFontSize(12)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1)).SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5));

            document.Add(totalsTable);

            document.Add(new Paragraph("\n\nThank you for your business!")
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFont(fontItalic)
                .SetFontColor(ColorConstants.GRAY));

            document.Close();
            return stream.ToArray();
        }
        public byte[] GenerateSalesAnalysisPdf(StandardReportData data)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

            document.SetFont(fontNormal);

            // --- 1. REPORT HEADER ---
            document.Add(new Paragraph((data.CompanyName ?? "COMPANY NAME").ToUpper())
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(14)
                .SetFont(fontBold)
                .SetMarginBottom(0));

            document.Add(new Paragraph(data.ReportName ?? "Sales Analysis Report")
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(12)
                .SetFont(fontBold)
                .SetMarginBottom(2));

            document.Add(new Paragraph($"Reporting Period: {data.ReportingPeriod ?? "N/A"}")
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(10)
                .SetFontColor(ColorConstants.DARK_GRAY)
                .SetMarginBottom(15));

            // --- 2. DATA TABLE STRUCTURE ---
            if (data.Headers != null && data.Headers.Any())
            {
                // Strict Column Matrices: Date/Ref, Customer Name, Type, Qty, Price, Amount
                float[] columnWidths = { 3.5f, 5f, 2.5f, 2.2f, 2.5f, 2.8f };
                bool[] isNumericColumn = { false, false, false, true, true, true };

                var table = new Table(UnitValue.CreatePercentArray(columnWidths)).UseAllAvailableWidth();

                // Column Label Headers Initialization
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

                // Row Matrix Population Loop
                if (data.Rows != null && data.Rows.Any())
                {
                    foreach (var row in data.Rows)
                    {
                        string trackingToken = row[0] ?? "";

                        if (trackingToken == "SECTION_SPACER")
                        {
                            for (int i = 0; i < data.Headers.Count; i++)
                            {
                                table.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetHeight(4f));
                            }
                            continue;
                        }

                        if (trackingToken.StartsWith("SECTION_HEADER:"))
                        {
                            string cleanedItemName = trackingToken.Replace("SECTION_HEADER:", "");
                            string typeLabel = row.Count > 1 ? row[1] : "";
                            string totalQty = row.Count > 3 ? row[3] : "0.00";
                            string totalAmount = row.Count > 5 ? row[5] : "0.00";

                            table.AddCell(new Cell(1, 2).Add(new Paragraph(cleanedItemName).SetFont(fontBold).SetFontSize(9)).SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetPadding(4f).SetBorder(Border.NO_BORDER));
                            table.AddCell(new Cell().Add(new Paragraph(typeLabel).SetFont(fontBold).SetFontSize(8).SetTextAlignment(TextAlignment.CENTER)).SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetPadding(4f).SetBorder(Border.NO_BORDER));
                            table.AddCell(new Cell().Add(new Paragraph(totalQty).SetFont(fontBold).SetFontSize(9).SetTextAlignment(TextAlignment.RIGHT)).SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetPadding(4f).SetBorder(Border.NO_BORDER));
                            table.AddCell(new Cell().SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetBorder(Border.NO_BORDER));
                            table.AddCell(new Cell().Add(new Paragraph(totalAmount).SetFont(fontBold).SetFontSize(9).SetTextAlignment(TextAlignment.RIGHT)).SetBackgroundColor(ColorConstants.LIGHT_GRAY).SetPadding(4f).SetBorder(Border.NO_BORDER));
                            continue;
                        }

                        if (trackingToken.StartsWith("REPORT_TOTAL:"))
                        {
                            string totalLabel = trackingToken.Replace("REPORT_TOTAL:", "");
                            string grandQty = row.Count > 3 ? row[3] : "0.00";
                            string grandAmount = row.Count > 5 ? row[5] : "0.00";

                            table.AddCell(new Cell(1, 3).Add(new Paragraph(totalLabel).SetFont(fontBold).SetFontSize(10)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1f)).SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1.5f)).SetPaddingTop(4f));
                            table.AddCell(new Cell().Add(new Paragraph(grandQty).SetFont(fontBold).SetFontSize(10).SetTextAlignment(TextAlignment.RIGHT)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1f)).SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1.5f)).SetPaddingTop(4f));
                            table.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1f)).SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1.5f)));
                            table.AddCell(new Cell().Add(new Paragraph(grandAmount).SetFont(fontBold).SetFontSize(10).SetTextAlignment(TextAlignment.RIGHT)).SetBorder(Border.NO_BORDER).SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1f)).SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1.5f)).SetPaddingTop(4f));
                            continue;
                        }

                        // Output Standard Details Row Cells
                        for (int i = 0; i < row.Count; i++)
                        {
                            string cellText = row[i] ?? "";
                            var p = new Paragraph(cellText).SetFontSize(8.5f);

                            Cell cell = new Cell().Add(p)
                                .SetBorder(Border.NO_BORDER)
                                .SetBorderBottom(new SolidBorder(ColorConstants.LIGHT_GRAY, 0.3f))
                                .SetPaddingTop(2f)
                                .SetPaddingBottom(2f)
                                .SetTextAlignment(isNumericColumn[i] ? TextAlignment.RIGHT : TextAlignment.LEFT);

                            table.AddCell(cell);
                        }
                    }
                }

                document.Add(table);
            }

            // --- 3. FOOTER ---
            document.Add(new Paragraph($"\nGenerated By: {data.GeneratedBy ?? "System User"} on {data.DateGenerated:yyyy-MM-dd HH:mm}")
                .SetTextAlignment(TextAlignment.LEFT)
                .SetFontSize(8)
                .SetFont(fontItalic)
                .SetFontColor(ColorConstants.GRAY));

            document.Close();
            return stream.ToArray();
        }

        /// <summary>
        /// Isolated Excel Generator built specifically to parse structured item sales ledger headers.
        /// </summary>
        public byte[] GenerateSalesAnalysisExcel(StandardReportData data)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Sales Ledger");

            // --- 1. CORPORATE HEADER TITLE ROWS ---
            worksheet.Cell(1, 1).Value = (data.CompanyName ?? "COMPANY NAME").ToUpper();
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 14;

            worksheet.Cell(2, 1).Value = data.ReportName ?? "Sales Analysis Report";
            worksheet.Cell(2, 1).Style.Font.Bold = true;
            worksheet.Cell(2, 1).Style.Font.FontSize = 12;

            worksheet.Cell(3, 1).Value = $"Period: {data.ReportingPeriod ?? "N/A"}";
            worksheet.Cell(3, 1).Style.Font.FontColor = XLColor.DarkGray;

            int currentRow = 5;

            // --- 2. DATA GRID STRUCTURE ---
            if (data.Headers != null && data.Headers.Any())
            {
                bool[] isNumericColumn = { false, false, false, true, true, true };

                // Build Table Columns Headers
                for (int i = 0; i < data.Headers.Count; i++)
                {
                    var cell = worksheet.Cell(currentRow, i + 1);
                    cell.Value = data.Headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");

                    if (isNumericColumn[i])
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                currentRow++;

                // Build Table Rows
                if (data.Rows != null && data.Rows.Any())
                {
                    foreach (var row in data.Rows)
                    {
                        string trackingToken = row[0] ?? "";

                        if (trackingToken == "SECTION_SPACER")
                        {
                            currentRow++;
                            continue;
                        }

                        if (trackingToken.StartsWith("SECTION_HEADER:"))
                        {
                            string cleanedItemName = trackingToken.Replace("SECTION_HEADER:", "");

                            var cellMain = worksheet.Cell(currentRow, 1);
                            cellMain.Value = cleanedItemName;
                            cellMain.Style.Font.Bold = true;

                            worksheet.Cell(currentRow, 2).Value = "";

                            var cellType = worksheet.Cell(currentRow, 3);
                            cellType.Value = row[1];
                            cellType.Style.Font.Bold = true;
                            cellType.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                            if (decimal.TryParse(row[3].Replace(",", ""), out decimal qGroup))
                            {
                                var cQ = worksheet.Cell(currentRow, 4);
                                cQ.Value = qGroup;
                                cQ.Style.Font.Bold = true;
                                cQ.Style.NumberFormat.Format = "#,##0.00";
                            }
                            if (decimal.TryParse(row[5].Replace(",", ""), out decimal aGroup))
                            {
                                var cA = worksheet.Cell(currentRow, 6);
                                cA.Value = aGroup;
                                cA.Style.Font.Bold = true;
                                cA.Style.NumberFormat.Format = "#,##0.00";
                            }

                            var rowRange = worksheet.Range(currentRow, 1, currentRow, data.Headers.Count);
                            rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                            rowRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                            rowRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

                            currentRow++;
                            continue;
                        }

                        if (trackingToken.StartsWith("REPORT_TOTAL:"))
                        {
                            string totalLabel = trackingToken.Replace("REPORT_TOTAL:", "");

                            var cellLabel = worksheet.Cell(currentRow, 1);
                            cellLabel.Value = totalLabel;
                            cellLabel.Style.Font.Bold = true;

                            if (decimal.TryParse(row[3].Replace(",", ""), out decimal qGrand))
                            {
                                var cQ = worksheet.Cell(currentRow, 4);
                                cQ.Value = qGrand;
                                cQ.Style.Font.Bold = true;
                                cQ.Style.NumberFormat.Format = "#,##0.00";
                            }
                            if (decimal.TryParse(row[5].Replace(",", ""), out decimal aGrand))
                            {
                                var cA = worksheet.Cell(currentRow, 6);
                                cA.Value = aGrand;
                                cA.Style.Font.Bold = true;
                                cA.Style.NumberFormat.Format = "#,##0.00";
                            }

                            var totalRange = worksheet.Range(currentRow, 1, currentRow, data.Headers.Count);
                            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                            totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;

                            currentRow++;
                            continue;
                        }

                        // Write Standard Transaction Rows Data Cells
                        for (int c = 0; c < row.Count; c++)
                        {
                            var cell = worksheet.Cell(currentRow, c + 1);
                            string cellText = row[c] ?? "";

                            if (isNumericColumn[c] && decimal.TryParse(cellText.Replace(",", ""), out decimal numericValue))
                            {
                                cell.Value = numericValue;
                                cell.Style.NumberFormat.Format = "#,##0.00";
                            }
                            else
                            {
                                cell.Value = cellText;
                            }
                        }
                        currentRow++;
                    }
                }

                worksheet.Columns().AdjustToContents();
            }

            // --- 3. FOOTER LOGS ---
            currentRow += 2;
            worksheet.Cell(currentRow, 1).Value = $"Generated By: {data.GeneratedBy ?? "System User"} on {data.DateGenerated:yyyy-MM-dd HH:mm}";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;

            using var memStream = new MemoryStream();
            workbook.SaveAs(memStream);
            return memStream.ToArray();
        }
    }
}
