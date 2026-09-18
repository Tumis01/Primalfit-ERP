using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;
using ClosedXML.Excel;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
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
        public async Task<CustomerStatementReport> GenerateCustomerStatementAsync(
    Guid companyId,
    Guid customerId,
    DateOnly startDate,
    DateOnly endDate,
    string generatedBy = "")
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var company = await ctx.CompanyDetails.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyDetailsId == companyId);

            var customers = await ctx.Customers
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId && (customerId == Guid.Empty || c.Id == customerId))
                .OrderBy(c => c.Name)
                .ToListAsync();

            var customerIds = customers.Select(c => c.Id).ToHashSet();

            var taxes = await ctx.Taxes
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId)
                .ToDictionaryAsync(t => t.Id, t => t.Per);

            var invoices = await ctx.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Where(o => o.CompanyId == companyId
                    && customerIds.Contains(o.CustomerId)
                    && o.Date <= endDate
                    && o.OrderNumber.StartsWith("INV")
                    && (o.Status == OrderStatus.Invoiced || o.Status == OrderStatus.PartiallyInvoiced))
                .ToListAsync();

            var payments = await ctx.CustomerPayments
                .AsNoTracking()
                .Include(p => p.Applications)
                .Where(p => p.CompanyId == companyId
                    && customerIds.Contains(p.CustomerId)
                    && p.Status == PaymentStatus.Posted
                    && p.Date <= endDate.ToDateTime(TimeOnly.MaxValue))
                .ToListAsync();

            var creditNotes = await ctx.CreditNotes
                .AsNoTracking()
                .Where(cn => cn.CompanyId == companyId
                    && customerIds.Contains(cn.CustomerId)
                    && cn.Status == CreditNoteStatus.Posted
                    && cn.Date <= endDate)
                .ToListAsync();

            // Customer receipt refunds restore the customer's AR balance when
            // cash is paid back (DR AR / CR bank). Quantity-only returns only
            // affect inventory and COGS, so only PaymentOnly and Both belong on
            // the customer statement.
            var receiptRefunds = await ctx.ReceiptRefunds
                .AsNoTracking()
                .Include(r => r.SalesOrder)
                .Where(r => r.CompanyId == companyId
                    && customerIds.Contains(r.CustomerId)
                    && r.Status == ReceiptRefundStatus.Posted
                    && (r.RefundType == ReceiptRefundType.PaymentOnly || r.RefundType == ReceiptRefundType.Both)
                    && r.Date <= endDate)
                .ToListAsync();

            var arAccountIds = customers
                .Where(c => c.ReceivablesAccountId.HasValue)
                .Select(c => c.ReceivablesAccountId!.Value)
                .ToHashSet();

            var customerOpeningBalanceMap = await ctx.TransactionGlMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CompanyId == companyId
                    && m.TransactionType == SystemTransactionType.ArAdjustment);

            var arAdjustmentMap = await ctx.TransactionGlMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CompanyId == companyId
                    && m.TransactionType == SystemTransactionType.ArAdjustment);

            if (customerOpeningBalanceMap?.OverrideDebitGlAccountId != null)
                arAccountIds.Add(customerOpeningBalanceMap.OverrideDebitGlAccountId.Value);

            if (arAdjustmentMap?.OverrideDebitGlAccountId != null)
                arAccountIds.Add(arAdjustmentMap.OverrideDebitGlAccountId.Value);

            var glAdjustments = await ctx.GLTransactions
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId
                    && t.PostingDate <= endDate
                    && arAccountIds.Contains(t.SegCoaId)
                    && t.Narration != null
                    && (
                        t.Narration.StartsWith("OB Dr:")
                        || t.Narration.StartsWith("OB Cr:")
                        || t.Narration.StartsWith("AR Adj Dr -")
                        || t.Narration.StartsWith("AR Adj Cr -")
                    ))
                .ToListAsync();

            var report = new CustomerStatementReport
            {
                ReportName = "Customer Statement",
                ReportingPeriod = $"{startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}",
                CompanyName = company?.CompanyName ?? "",
                CompanyAddress = company?.PhysicalAddress ?? company?.PostalAddress ?? "",
                CompanyEmail = company?.CompanyEmail ?? "",
                CompanyRegNo = company?.ComanyRegNumber ?? "",
                GeneratedBy = generatedBy,
                DateGenerated = DateTime.Now
            };

            foreach (var customer in customers)
            {
                var activity = new List<CustomerStatementLine>();

                foreach (var invoice in invoices.Where(i => i.CustomerId == customer.Id))
                {
                    decimal invoiceTotal = CalculateInvoiceTotal(invoice, taxes);
                    decimal rate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1;

                    activity.Add(new CustomerStatementLine
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        CustomerAddress = customer.Address ?? "",
                        Date = invoice.Date,
                        DocumentNumber = invoice.OrderNumber,
                        Type = "Invoice",
                        Description = invoice.IsDirectInvoice ? "Direct sales invoice" : "Sales invoice",
                        Debit = Math.Round(invoiceTotal * rate, 2)
                    });
                }

                foreach (var payment in payments.Where(p => p.CustomerId == customer.Id))
                {
                    decimal rate = payment.ExchangeRate > 0 ? payment.ExchangeRate : 1;
                    decimal cashDiscount = payment.Applications.Sum(a => a.CashDiscountTaken);

                    activity.Add(new CustomerStatementLine
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        CustomerAddress = customer.Address ?? "",
                        Date = DateOnly.FromDateTime(payment.Date),
                        DocumentNumber = payment.Reference,
                        Type = "Receipt",
                        Description = cashDiscount > 0 ? "Customer receipt / cash discount" : "Customer receipt",
                        Credit = Math.Round((payment.AmountReceived + cashDiscount) * rate, 2)
                    });
                }

                foreach (var creditNote in creditNotes.Where(cn => cn.CustomerId == customer.Id))
                {
                    decimal rate = creditNote.ExchangeRate > 0 ? creditNote.ExchangeRate : 1;

                    activity.Add(new CustomerStatementLine
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        CustomerAddress = customer.Address ?? "",
                        Date = creditNote.Date,
                        DocumentNumber = creditNote.CreditNoteNumber,
                        Type = "Credit Note",
                        Description = string.IsNullOrWhiteSpace(creditNote.Reason) ? "Credit note" : creditNote.Reason,
                        Credit = Math.Round(creditNote.TotalAmount * rate, 2)
                    });
                }

                foreach (var receiptRefund in receiptRefunds.Where(rr => rr.CustomerId == customer.Id))
                {
                    decimal rate = receiptRefund.ExchangeRate > 0 ? receiptRefund.ExchangeRate : 1m;
                    decimal amountBase = Math.Round(receiptRefund.TotalAmount * rate, 2);
                    if (amountBase <= 0) continue;

                    string invoiceReference = receiptRefund.SalesOrder?.OrderNumber ?? "sales invoice";
                    activity.Add(new CustomerStatementLine
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        CustomerAddress = customer.Address ?? "",
                        Date = receiptRefund.Date,
                        DocumentNumber = receiptRefund.RefundNumber,
                        Type = "Receipt Refund - Cash",
                        Description = string.IsNullOrWhiteSpace(receiptRefund.Reason)
                            ? $"Cash refund paid to customer against {invoiceReference}"
                                + (receiptRefund.RefundType == ReceiptRefundType.Both
                                    ? "; stock return recorded separately in inventory/COGS"
                                    : "")
                            : receiptRefund.Reason,
                        Debit = amountBase
                    });
                }

                foreach (var adj in glAdjustments.Where(a => IsCustomerAdjustmentFor(a.Narration, customer.Name)))
                {
                    activity.Add(new CustomerStatementLine
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        CustomerAddress = customer.Address ?? "",
                        Date = adj.PostingDate,
                        DocumentNumber = "GL Adjustment",
                        Type = adj.Narration!.StartsWith("OB") ? "Opening Balance Adjustment" : "AR Adjustment",
                        Description = adj.Narration ?? "",
                        Debit = adj.Debit,
                        Credit = adj.Credit
                    });
                }

                decimal runningBalance = activity
                    .Where(a => a.Date < startDate)
                    .Sum(a => a.Debit - a.Credit);

                report.Lines.Add(new CustomerStatementLine
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    CustomerAddress = customer.Address ?? "",
                    Type = "Opening Balance",
                    Description = $"Opening balance for {customer.Name}",
                    Balance = runningBalance,
                    IsBalanceRow = true
                });

                foreach (var line in activity
                    .Where(a => a.Date >= startDate && a.Date <= endDate)
                    .OrderBy(a => a.Date)
                    .ThenBy(a => a.Type)
                    .ThenBy(a => a.DocumentNumber))
                {
                    runningBalance += line.Debit - line.Credit;
                    line.Balance = runningBalance;
                    report.Lines.Add(line);
                }

                report.Lines.Add(new CustomerStatementLine
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    CustomerAddress = customer.Address ?? "",
                    Type = "Closing Balance",
                    Description = $"Closing balance for {customer.Name}",
                    Balance = runningBalance,
                    IsBalanceRow = true
                });
            }

            return report;
        }

        public byte[] GenerateCustomerStatementPdf(CustomerStatementReport report)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            pdf.SetDefaultPageSize(PageSize.A4.Rotate());

            using var document = new Document(pdf);
            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            document.SetFont(fontNormal).SetFontSize(8);

            document.Add(new Paragraph(report.CompanyName.ToUpper())
                .SetFont(fontBold)
                .SetFontSize(14)
                .SetTextAlignment(TextAlignment.CENTER));

            if (!string.IsNullOrWhiteSpace(report.CompanyAddress))
            {
                document.Add(new Paragraph(report.CompanyAddress)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetFontSize(9));
            }

            document.Add(new Paragraph($"{report.CompanyEmail} {report.CompanyRegNo}".Trim())
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(9));

            document.Add(new Paragraph(report.ReportName)
                .SetFont(fontBold)
                .SetFontSize(12)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginTop(10));

            document.Add(new Paragraph(report.ReportingPeriod)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(9)
                .SetMarginBottom(8));

            var addressLines = report.Lines
                .Where(x => !string.IsNullOrWhiteSpace(x.CustomerName))
                .GroupBy(x => x.CustomerId)
                .Select(g => g.First())
                .Where(x => !string.IsNullOrWhiteSpace(x.CustomerAddress))
                .ToList();

            

            var table = new Table(UnitValue.CreatePercentArray(new float[] { 1.1f, 2.2f, 1.6f, 1.5f, 3f, 1.3f, 1.3f, 1.4f }))
                .UseAllAvailableWidth();

            string[] headers = { "Date", "Customer", "Reference", "Type", "Description", "Debit", "Credit", "Balance" };

            foreach (var header in headers)
            {
                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph(header).SetFont(fontBold).SetFontSize(7))
                    .SetBorder(Border.NO_BORDER)
                    .SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1))
                    .SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1))
                    .SetTextAlignment(IsMoneyHeader(header) ? TextAlignment.RIGHT : TextAlignment.LEFT));
            }

            foreach (var line in report.Lines)
            {
                AddPdfCell(table, line.Date?.ToString("yyyy-MM-dd") ?? "", line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.CustomerName, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.DocumentNumber, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.Type, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.Description, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.Debit == 0 ? "-" : FormatMoney(line.Debit), line.IsBalanceRow, true, fontBold);
                AddPdfCell(table, line.Credit == 0 ? "-" : FormatMoney(line.Credit), line.IsBalanceRow, true, fontBold);
                AddPdfCell(table, FormatMoney(line.Balance), line.IsBalanceRow, true, fontBold);
            }

            document.Add(table);

            document.Add(new Paragraph($"\nGenerated By: {report.GeneratedBy} on {report.DateGenerated:yyyy-MM-dd HH:mm}")
                .SetFontSize(8)
                .SetFontColor(ColorConstants.GRAY));

            document.Close();
            return stream.ToArray();
        }

        public byte[] GenerateCustomerStatementExcel(CustomerStatementReport report)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Customer Statement");

            ws.Cell(1, 1).Value = report.ReportName;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Value = report.ReportingPeriod;

            string[] headers = { "Date", "Customer", "Reference", "Type", "Description", "Debit", "Credit", "Balance" };

            int row = 4;

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(row, i + 1).Value = headers[i];
                ws.Cell(row, i + 1).Style.Font.Bold = true;
                ws.Cell(row, i + 1).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }

            row++;

            foreach (var line in report.Lines)
            {
                ws.Cell(row, 1).Value = line.Date?.ToString("yyyy-MM-dd") ?? "";
                ws.Cell(row, 2).Value = line.CustomerName;
                ws.Cell(row, 3).Value = line.DocumentNumber;
                ws.Cell(row, 4).Value = line.Type;
                ws.Cell(row, 5).Value = line.Description;
                ws.Cell(row, 6).Value = line.Debit == 0 ? "-" : FormatMoney(line.Debit);
                ws.Cell(row, 7).Value = line.Credit == 0 ? "-" : FormatMoney(line.Credit);
                ws.Cell(row, 8).Value = FormatMoney(line.Balance);

                if (line.IsBalanceRow)
                {
                    ws.Range(row, 1, row, 8).Style.Font.Bold = true;
                    ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }

                row++;
            }

            ws.Columns().AdjustToContents();

            using var mem = new MemoryStream();
            workbook.SaveAs(mem);
            return mem.ToArray();
        }


        private static decimal CalculateInvoiceTotal(SalesOrder order, Dictionary<Guid, decimal> taxes)
        {
            decimal subTotal = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

            decimal discount = order.DiscountPercentage > 0
                ? subTotal * (order.DiscountPercentage / 100)
                : order.DiscountAmount;

            decimal net = subTotal - discount;

            decimal taxRate = order.TaxId.HasValue && taxes.ContainsKey(order.TaxId.Value)
                ? taxes[order.TaxId.Value]
                : 0;

            return net + (net * taxRate / 100);
        }

        private static bool IsCustomerAdjustmentFor(string? narration, string customerName)
        {
            if (string.IsNullOrWhiteSpace(narration) || string.IsNullOrWhiteSpace(customerName))
                return false;

            return narration.EndsWith($": {customerName}", StringComparison.OrdinalIgnoreCase)
                || narration.EndsWith($"- {customerName}", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatMoney(decimal value)
        {
            return value < 0
                ? $"({Math.Abs(value):N2})"
                : value.ToString("N2");
        }

        private static bool IsMoneyHeader(string header)
        {
            return header is "Debit" or "Credit" or "Balance";
        }

        private static void AddPdfCell(Table table, string text, bool isBold, bool rightAlign, PdfFont fontBold)
        {
            var paragraph = new Paragraph(text ?? "").SetFontSize(7);

            if (isBold)
                paragraph.SetFont(fontBold);

            table.AddCell(new Cell()
                .Add(paragraph)
                .SetBorder(Border.NO_BORDER)
                .SetBorderBottom(new SolidBorder(ColorConstants.LIGHT_GRAY, 0.3f))
                .SetPadding(3)
                .SetTextAlignment(rightAlign ? TextAlignment.RIGHT : TextAlignment.LEFT));
        }
        public async Task<VendorStatementReport> GenerateVendorStatementAsync(
    Guid companyId,
    Guid vendorId,
    DateOnly startDate,
    DateOnly endDate,
    string generatedBy = "")
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var company = await ctx.CompanyDetails.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyDetailsId == companyId);

            var vendors = await ctx.Vendors
                .AsNoTracking()
                .Where(v => v.CompanyId == companyId && (vendorId == Guid.Empty || v.Id == vendorId))
                .OrderBy(v => v.Name)
                .ToListAsync();

            var vendorIds = vendors.Select(v => v.Id).ToHashSet();

            var bills = await ctx.VendorBills
                .AsNoTracking()
                .Include(b => b.Payments)
                .Where(b => b.CompanyId == companyId
                    && vendorIds.Contains(b.VendorId)
                    && b.IsPosted
                    && b.BillDate <= endDate.ToDateTime(TimeOnly.MaxValue))
                .ToListAsync();

            // Debit notes reduce the vendor AP balance. TotalAmount is stored in
            // the document currency, so convert it to the same base currency used
            // by VendorBill.TotalAmount and VendorPayment.Amount in this report.
            var debitNotes = await ctx.DebitNotes
                .AsNoTracking()
                .Include(d => d.PurchaseOrder)
                .Where(d => d.CompanyId == companyId
                    && vendorIds.Contains(d.VendorId)
                    && d.Status == DebitNoteStatus.Posted
                    && d.Date <= endDate)
                .ToListAsync();

            // Vendor returns have two possible AP effects:
            //   - physical stock return: DR AP (debit / liability reduction)
            //   - cash refund: CR AP (credit / liability restoration)
            // For Both, a single statement line carries both sides so the row
            // remains visible while the net AP movement stays faithful to the GL.
            var vendorReturns = await ctx.VendorReturns
                .AsNoTracking()
                .Include(r => r.Lines)
                .Include(r => r.PurchaseOrder)
                    .ThenInclude(po => po!.Lines)
                .Where(r => r.CompanyId == companyId
                    && vendorIds.Contains(r.VendorId)
                    && r.Status == VendorReturnStatus.Posted
                    && r.Date <= endDate)
                .ToListAsync();

            var apAccountIds = vendors
                .Where(v => v.PayablesAccountId.HasValue)
                .Select(v => v.PayablesAccountId!.Value)
                .ToHashSet();

            var vendorOpeningBalanceMap = await ctx.TransactionGlMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CompanyId == companyId
                    && m.TransactionType == SystemTransactionType.ApAdjustment);

            var apAdjustmentMap = await ctx.TransactionGlMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CompanyId == companyId
                    && m.TransactionType == SystemTransactionType.ApAdjustment);

            if (vendorOpeningBalanceMap?.OverrideCreditGlAccountId != null)
                apAccountIds.Add(vendorOpeningBalanceMap.OverrideCreditGlAccountId.Value);

            if (apAdjustmentMap?.OverrideDebitGlAccountId != null)
                apAccountIds.Add(apAdjustmentMap.OverrideDebitGlAccountId.Value);

            var glAdjustments = await ctx.GLTransactions
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId
                    && t.PostingDate <= endDate
                    && apAccountIds.Contains(t.SegCoaId)
                    && t.Narration != null
                    && (
                        t.Narration.StartsWith("OB Dr:")
                        || t.Narration.StartsWith("OB Cr:")
                        || t.Narration.StartsWith("AP Adj Dr -")
                        || t.Narration.StartsWith("AP Adj Cr -")
                    ))
                .ToListAsync();

            var report = new VendorStatementReport
            {
                ReportName = "Vendor Statement",
                ReportingPeriod = $"{startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}",
                CompanyName = company?.CompanyName ?? "",
                CompanyAddress = company?.PhysicalAddress ?? company?.PostalAddress ?? "",
                CompanyEmail = company?.CompanyEmail ?? "",
                CompanyRegNo = company?.ComanyRegNumber ?? "",
                GeneratedBy = generatedBy,
                DateGenerated = DateTime.Now
            };

            foreach (var vendor in vendors)
            {
                var activity = new List<VendorStatementLine>();

                foreach (var bill in bills.Where(b => b.VendorId == vendor.Id))
                {
                    activity.Add(new VendorStatementLine
                    {
                        VendorId = vendor.Id,
                        VendorName = vendor.Name,
                        VendorAddress = vendor.Address ?? "",
                        Date = DateOnly.FromDateTime(bill.BillDate),
                        DocumentNumber = bill.ExternalInvoiceNumber,
                        Type = bill.IsDirectBill ? "Direct Bill" : "Vendor Bill",
                        Description = string.IsNullOrWhiteSpace(bill.Description) ? "Vendor invoice" : bill.Description,
                        Credit = bill.TotalAmount
                    });

                    foreach (var payment in bill.Payments.Where(p => p.Date <= endDate.ToDateTime(TimeOnly.MaxValue)))
                    {
                        activity.Add(new VendorStatementLine
                        {
                            VendorId = vendor.Id,
                            VendorName = vendor.Name,
                            VendorAddress = vendor.Address ?? "",
                            Date = DateOnly.FromDateTime(payment.Date),
                            DocumentNumber = payment.Reference,
                            Type = "Payment",
                            Description = $"Payment for {bill.ExternalInvoiceNumber}",
                            Debit = payment.Amount
                        });
                    }
                }

                foreach (var debitNote in debitNotes.Where(d => d.VendorId == vendor.Id))
                {
                    decimal rate = debitNote.ExchangeRate > 0 ? debitNote.ExchangeRate : 1m;
                    decimal amountBase = Math.Round(debitNote.TotalAmount * rate, 2);
                    if (amountBase <= 0) continue;

                    string invoiceReference = debitNote.PurchaseOrder?.OrderNumber ?? "purchase invoice";
                    activity.Add(new VendorStatementLine
                    {
                        VendorId = vendor.Id,
                        VendorName = vendor.Name,
                        VendorAddress = vendor.Address ?? "",
                        Date = debitNote.Date,
                        DocumentNumber = debitNote.DebitNoteNumber,
                        Type = "Debit Note",
                        Description = string.IsNullOrWhiteSpace(debitNote.Reason)
                            ? $"Debit note against {invoiceReference}"
                            : debitNote.Reason,
                        Debit = amountBase
                    });
                }

                foreach (var vendorReturn in vendorReturns.Where(r => r.VendorId == vendor.Id))
                {
                    decimal rate = vendorReturn.ExchangeRate > 0 ? vendorReturn.ExchangeRate : 1m;
                    decimal stockReturnForeign = 0m;

                    if (vendorReturn.ReturnType == VendorReturnType.QuantityOnly
                        || vendorReturn.ReturnType == VendorReturnType.Both)
                    {
                        // Match VendorReturnService.PostVendorReturnAsync: line.Quantity
                        // is normalized to the primary UOM and the PO line UnitCost is
                        // the primary-UOM cost used in the GL posting.
                        stockReturnForeign = vendorReturn.Lines.Sum(line =>
                        {
                            var purchaseLine = vendorReturn.PurchaseOrder?.Lines
                                .FirstOrDefault(poLine => poLine.Id == line.PurchaseOrderLineId);
                            return line.Quantity * (purchaseLine?.UnitCost ?? line.UnitCost);
                        });
                    }

                    decimal stockReturnBase = Math.Round(stockReturnForeign * rate, 2);
                    decimal cashRefundBase = vendorReturn.ReturnType == VendorReturnType.PaymentOnly
                        || vendorReturn.ReturnType == VendorReturnType.Both
                        ? Math.Round(vendorReturn.TotalAmount * rate, 2)
                        : 0m;

                    if (stockReturnBase <= 0 && cashRefundBase <= 0) continue;

                    string returnType = vendorReturn.ReturnType switch
                    {
                        VendorReturnType.QuantityOnly => "Vendor Return - Stock",
                        VendorReturnType.PaymentOnly => "Vendor Return - Payment Refund",
                        _ => "Vendor Return - Stock & Payment Refund"
                    };

                    string defaultDescription = vendorReturn.ReturnType switch
                    {
                        VendorReturnType.QuantityOnly => "Physical stock returned to vendor",
                        VendorReturnType.PaymentOnly => "Cash refund received from vendor",
                        _ => "Physical stock returned and cash refund received from vendor"
                    };

                    activity.Add(new VendorStatementLine
                    {
                        VendorId = vendor.Id,
                        VendorName = vendor.Name,
                        VendorAddress = vendor.Address ?? "",
                        Date = vendorReturn.Date,
                        DocumentNumber = vendorReturn.ReturnNumber,
                        Type = returnType,
                        Description = string.IsNullOrWhiteSpace(vendorReturn.Reason)
                            ? defaultDescription
                            : vendorReturn.Reason,
                        Debit = stockReturnBase,
                        Credit = cashRefundBase
                    });
                }

                foreach (var adj in glAdjustments.Where(a => IsVendorAdjustmentFor(a.Narration, vendor.Name)))
                {
                    activity.Add(new VendorStatementLine
                    {
                        VendorId = vendor.Id,
                        VendorName = vendor.Name,
                        VendorAddress = vendor.Address ?? "",
                        Date = adj.PostingDate,
                        DocumentNumber = "GL Adjustment",
                        Type = adj.Narration!.StartsWith("OB") ? "Opening Balance Adjustment" : "AP Adjustment",
                        Description = adj.Narration ?? "",
                        Debit = adj.Debit,
                        Credit = adj.Credit
                    });
                }

                decimal runningBalance = activity
                    .Where(a => a.Date < startDate)
                    .Sum(a => a.Credit - a.Debit);

                report.Lines.Add(new VendorStatementLine
                {
                    VendorId = vendor.Id,
                    VendorName = vendor.Name,
                    VendorAddress = vendor.Address ?? "",
                    Type = "Opening Balance",
                    Description = $"Opening balance for {vendor.Name}",
                    Balance = runningBalance,
                    IsBalanceRow = true
                });

                foreach (var line in activity
                    .Where(a => a.Date >= startDate && a.Date <= endDate)
                    .OrderBy(a => a.Date)
                    .ThenBy(a => a.Type)
                    .ThenBy(a => a.DocumentNumber))
                {
                    runningBalance += line.Credit - line.Debit;
                    line.Balance = runningBalance;
                    report.Lines.Add(line);
                }

                report.Lines.Add(new VendorStatementLine
                {
                    VendorId = vendor.Id,
                    VendorName = vendor.Name,
                    VendorAddress = vendor.Address ?? "",
                    Type = "Closing Balance",
                    Description = $"Closing balance for {vendor.Name}",
                    Balance = runningBalance,
                    IsBalanceRow = true
                });
            }

            return report;
        }
        public byte[] GenerateVendorStatementPdf(VendorStatementReport report)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            pdf.SetDefaultPageSize(PageSize.A4.Rotate());

            using var document = new Document(pdf);
            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            document.SetFont(fontNormal).SetFontSize(8);

            document.Add(new Paragraph(report.CompanyName.ToUpper())
                .SetFont(fontBold)
                .SetFontSize(14)
                .SetTextAlignment(TextAlignment.CENTER));

            if (!string.IsNullOrWhiteSpace(report.CompanyAddress))
            {
                document.Add(new Paragraph(report.CompanyAddress)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetFontSize(9));
            }

            document.Add(new Paragraph($"{report.CompanyEmail} {report.CompanyRegNo}".Trim())
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(9));

            document.Add(new Paragraph(report.ReportName)
                .SetFont(fontBold)
                .SetFontSize(12)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginTop(10));

            document.Add(new Paragraph(report.ReportingPeriod)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetFontSize(9)
                .SetMarginBottom(8));

            var addressLines = report.Lines
                .GroupBy(x => x.VendorId)
                .Select(g => g.First())
                .Where(x => !string.IsNullOrWhiteSpace(x.VendorAddress))
                .ToList();


            var table = new Table(UnitValue.CreatePercentArray(new float[] { 1.1f, 2.2f, 1.6f, 1.5f, 3f, 1.3f, 1.3f, 1.4f }))
                .UseAllAvailableWidth();

            string[] headers = { "Date", "Vendor", "Reference", "Type", "Description", "Debit", "Credit", "Balance" };

            foreach (var header in headers)
            {
                table.AddHeaderCell(new Cell()
                    .Add(new Paragraph(header).SetFont(fontBold).SetFontSize(7))
                    .SetBorder(Border.NO_BORDER)
                    .SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1))
                    .SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 1))
                    .SetTextAlignment(IsMoneyHeader(header) ? TextAlignment.RIGHT : TextAlignment.LEFT));
            }

            foreach (var line in report.Lines)
            {
                AddPdfCell(table, line.Date?.ToString("yyyy-MM-dd") ?? "", line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.VendorName, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.DocumentNumber, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.Type, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.Description, line.IsBalanceRow, false, fontBold);
                AddPdfCell(table, line.Debit == 0 ? "-" : FormatMoney(line.Debit), line.IsBalanceRow, true, fontBold);
                AddPdfCell(table, line.Credit == 0 ? "-" : FormatMoney(line.Credit), line.IsBalanceRow, true, fontBold);
                AddPdfCell(table, FormatMoney(line.Balance), line.IsBalanceRow, true, fontBold);
            }

            document.Add(table);

            document.Add(new Paragraph($"\nGenerated By: {report.GeneratedBy} on {report.DateGenerated:yyyy-MM-dd HH:mm}")
                .SetFontSize(8)
                .SetFontColor(ColorConstants.GRAY));

            document.Close();
            return stream.ToArray();
        }

        public byte[] GenerateVendorStatementExcel(VendorStatementReport report)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Vendor Statement");

            ws.Cell(1, 1).Value = report.ReportName;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Value = report.ReportingPeriod;

            string[] headers = { "Date", "Vendor", "Reference", "Type", "Description", "Debit", "Credit", "Balance" };

            int row = 4;

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(row, i + 1).Value = headers[i];
                ws.Cell(row, i + 1).Style.Font.Bold = true;
                ws.Cell(row, i + 1).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }

            row++;

            foreach (var line in report.Lines)
            {
                ws.Cell(row, 1).Value = line.Date?.ToString("yyyy-MM-dd") ?? "";
                ws.Cell(row, 2).Value = line.VendorName;
                ws.Cell(row, 3).Value = line.DocumentNumber;
                ws.Cell(row, 4).Value = line.Type;
                ws.Cell(row, 5).Value = line.Description;
                ws.Cell(row, 6).Value = line.Debit == 0 ? "-" : FormatMoney(line.Debit);
                ws.Cell(row, 7).Value = line.Credit == 0 ? "-" : FormatMoney(line.Credit);
                ws.Cell(row, 8).Value = FormatMoney(line.Balance);

                if (line.IsBalanceRow)
                {
                    ws.Range(row, 1, row, 8).Style.Font.Bold = true;
                    ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }

                row++;
            }

            ws.Columns().AdjustToContents();

            using var mem = new MemoryStream();
            workbook.SaveAs(mem);
            return mem.ToArray();
        }
        private static bool IsVendorAdjustmentFor(string? narration, string vendorName)
        {
            if (string.IsNullOrWhiteSpace(narration) || string.IsNullOrWhiteSpace(vendorName))
                return false;

            return narration.EndsWith($": {vendorName}", StringComparison.OrdinalIgnoreCase)
                || narration.EndsWith($"- {vendorName}", StringComparison.OrdinalIgnoreCase);
        }

    }
}
