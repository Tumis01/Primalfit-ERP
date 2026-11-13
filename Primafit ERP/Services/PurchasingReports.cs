using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public partial class PurchasingService
{
    private static string ReportAmount(decimal value) => value.ToString("N4");

    private static StandardReportData PurchaseReport(string name, DateOnly start, DateOnly end, params string[] headers)
    {
        ReportAccounting.ValidatePeriod(start, end);
        return new() { ReportName = name, ReportingPeriod = $"{start:dd MMM yyyy} to {end:dd MMM yyyy}", Headers = headers.ToList() };
    }

    private static async Task<(List<PurchaseOrder> Orders, Dictionary<Guid, decimal> Received, Dictionary<Guid, string> Vendors, Dictionary<Guid, Item> Items)>
        ReceiptReportSourceAsync(AppDbContext db, Guid companyId, DateOnly start, DateOnly end, bool includeClosed = false)
    {
        ReportAccounting.ValidatePeriod(start, end);
        var from = start.ToDateTime(TimeOnly.MinValue); var to = end.ToDateTime(TimeOnly.MaxValue);
        var orders = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == companyId && x.OrderDate >= from && x.OrderDate <= to
                && x.Status != PurchaseOrderStatus.Request && x.Status != PurchaseOrderStatus.DraftInvoice
                && (includeClosed || x.Status != PurchaseOrderStatus.Closed) && x.Status != PurchaseOrderStatus.Cancelled
                && !x.IsDirectInvoice && x.ConvertedFromPONumber == null).ToListAsync();
        var lineIds = orders.SelectMany(x => x.Lines).Select(x => x.Id).ToList();
        var receiptIds = await db.GoodsReceipts.Where(x => x.CompanyId == companyId && x.DateReceived <= to).Select(x => x.Id).ToListAsync();
        var received = await ReportAccounting.PostedReceiptLines(db, companyId)
            .Where(x => x.PurchaseOrderLineId.HasValue && lineIds.Contains(x.PurchaseOrderLineId.Value) && receiptIds.Contains(x.GoodsReceiptId))
            .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.QuantityReceived) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
        var vendors = await db.Vendors.Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.Name);
        var itemIds = orders.SelectMany(x => x.Lines).Select(x => x.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(x => x.CompanyId == companyId && itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        return (orders, received, vendors, items);
    }

    public async Task<StandardReportData> GeneratePOAgingReportAsync(Guid companyId, DateOnly start, DateOnly end)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // An as-of open-order report must retain orders from earlier years.
        var source = await ReceiptReportSourceAsync(db, companyId, DateOnly.MinValue, end);
        var report = PurchaseReport("Open Purchase Order Aging", DateOnly.MinValue, end, "PO Number", "Vendor", "Order Date", "Age (Days)", "Item", "Primary UOM", "Qty Outstanding", "Outstanding Value (excl. tax)", "Aging Bucket");
        report.ReportingPeriod = $"As of {end:dd MMM yyyy} · Posted receipts only";
        decimal total = 0;
        foreach (var po in source.Orders.OrderBy(x => x.OrderDate))
        foreach (var line in po.Lines)
        {
            var remaining = Math.Max(0m, line.QuantityOrdered - source.Received.GetValueOrDefault(line.Id));
            if (remaining <= UomConversion.QuantityTolerance) continue;
            var days = end.DayNumber - DateOnly.FromDateTime(po.OrderDate).DayNumber;
            var value = ReportAccounting.Money(remaining * line.UnitCost * po.ExchangeRate);
            report.Rows.Add(new() { po.OrderNumber, source.Vendors.GetValueOrDefault(po.VendorId, "Unknown"), po.OrderDate.ToString("yyyy-MM-dd"), days.ToString(), source.Items.GetValueOrDefault(line.ItemId)?.Name ?? "Unknown", source.Items.GetValueOrDefault(line.ItemId)?.UoM ?? "", ReportAmount(remaining), ReportAmount(value), days <= 30 ? "0–30 days" : days <= 60 ? "31–60 days" : days <= 90 ? "61–90 days" : "90+ days" });
            total += value;
        }
        report.Rows.Add(new() { "", "GRAND TOTAL", "", "", "", "", "", ReportAmount(total), "" });
        return report;
    }

    public async Task<StandardReportData> GenerateOverUnderReceivingReportAsync(Guid companyId, DateOnly start, DateOnly end)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var source = await ReceiptReportSourceAsync(db, companyId, start, end, true);
        var report = PurchaseReport("Over / Under Receiving", start, end, "PO Number", "Vendor", "Item", "Primary UOM", "Qty Ordered", "Qty Received", "Variance", "Status");
        foreach (var po in source.Orders.Where(p => p.Lines.Any(l => source.Received.GetValueOrDefault(l.Id) > 0)))
        foreach (var line in po.Lines)
        {
            var received = source.Received.GetValueOrDefault(line.Id); var variance = received - line.QuantityOrdered;
            report.Rows.Add(new() { po.OrderNumber, source.Vendors.GetValueOrDefault(po.VendorId, "Unknown"), source.Items.GetValueOrDefault(line.ItemId)?.Name ?? "Unknown", source.Items.GetValueOrDefault(line.ItemId)?.UoM ?? "", ReportAmount(line.QuantityOrdered), ReportAmount(received), ReportAmount(variance), Math.Abs(variance) <= UomConversion.QuantityTolerance ? "Exact" : variance > 0 ? "Over received" : "Under received" });
        }
        report.ReportingPeriod += " · Quantities include posted receipts through the end date";
        return report;
    }

    public async Task<StandardReportData> GeneratePartiallyReceivedPOsReportAsync(Guid companyId, DateOnly start, DateOnly end)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var source = await ReceiptReportSourceAsync(db, companyId, start, end);
        var report = PurchaseReport("Partially Received Purchase Orders", start, end, "PO Number", "Vendor", "Item", "Primary UOM", "Qty Ordered", "Qty Received", "Qty Remaining", "% Received", "Remaining Value (excl. tax)");
        decimal total = 0;
        foreach (var po in source.Orders.Where(p => p.Lines.Any(l => source.Received.GetValueOrDefault(l.Id) > 0)))
        foreach (var line in po.Lines)
        {
            var received = source.Received.GetValueOrDefault(line.Id); var remaining = Math.Max(0, line.QuantityOrdered - received);
            if (remaining <= UomConversion.QuantityTolerance) continue;
            var value = ReportAccounting.Money(remaining * line.UnitCost * po.ExchangeRate); total += value;
            report.Rows.Add(new() { po.OrderNumber, source.Vendors.GetValueOrDefault(po.VendorId, "Unknown"), source.Items.GetValueOrDefault(line.ItemId)?.Name ?? "Unknown", source.Items.GetValueOrDefault(line.ItemId)?.UoM ?? "", ReportAmount(line.QuantityOrdered), ReportAmount(received), ReportAmount(remaining), (line.QuantityOrdered == 0 ? 0 : received / line.QuantityOrdered * 100).ToString("N2") + "%", ReportAmount(value) });
        }
        report.Rows.Add(new() { "", "GRAND TOTAL", "", "", "", "", "", "", ReportAmount(total) });
        return report;
    }

    public async Task<StandardReportData> GenerateVendorPaymentHistoryReportAsync(Guid companyId, DateOnly start, DateOnly end)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var report = PurchaseReport("Vendor Payment History", start, end, "Payment Date", "Vendor", "Invoice Number", "Payment Reference", "Bank Account", "Cash Amount", "Withholding Amount", "Total Settlement");
        var from = start.ToDateTime(TimeOnly.MinValue); var to = end.ToDateTime(TimeOnly.MaxValue);
        var payments = await ReportAccounting.PostedVendorPayments(db, companyId).Include(x => x.PurchaseInvoice).ThenInclude(x => x!.Vendor)
            .Where(x => x.Date >= from && x.Date <= to).OrderBy(x => x.Date).ToListAsync();
        var accounts = await db.SegChartOfAccounts.Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.Description);
        foreach (var p in payments) report.Rows.Add(new() { p.Date.ToString("yyyy-MM-dd"), p.PurchaseInvoice?.Vendor?.Name ?? "Unknown", p.PurchaseInvoice?.PurchaseInvoiceNumber ?? p.PurchaseInvoice?.ExternalInvoiceNumber ?? "", p.Reference, accounts.GetValueOrDefault(p.BankGlAccountId, "Unknown"), ReportAmount(p.Amount), ReportAmount(p.WithholdingAmount), ReportAmount(p.Amount + p.WithholdingAmount) });
        report.Rows.Add(new() { "", "GRAND TOTAL", "", "", "", ReportAmount(payments.Sum(x => x.Amount)), ReportAmount(payments.Sum(x => x.WithholdingAmount)), ReportAmount(payments.Sum(x => x.Amount + x.WithholdingAmount)) });
        return report;
    }

    public async Task<StandardReportData> GeneratePurchaseSpendByVendorReportAsync(Guid companyId, DateOnly start, DateOnly end)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var report = PurchaseReport("Purchase Spend by Vendor", start, end, "Vendor", "No. of Bills", "Total Billed", "Debit Notes / Stock Returns", "Total Settlement (net refunds)", "Outstanding Balance");
        var from = start.ToDateTime(TimeOnly.MinValue); var to = end.ToDateTime(TimeOnly.MaxValue);
        var bills = await ReportAccounting.PostedBills(db, companyId).Include(x => x.Vendor).Where(x => x.BillDate >= from && x.BillDate <= to).ToListAsync();
        var ids = bills.Select(x => x.Id).ToList();
        var payments = await ReportAccounting.PostedVendorPayments(db, companyId).Where(x => ids.Contains(x.PurchaseInvoiceId) && x.Date <= to).ToListAsync();
        var debitNotes = await db.DebitNotes.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == DebitNoteStatus.Posted && x.Date <= end
            && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId && b.CompanyId == companyId && b.Status == BatchStatus.Posted))).ToListAsync();
        var returns = await db.VendorReturns.AsNoTracking().Include(x => x.Lines).Where(x => x.CompanyId == companyId && x.PurchaseInvoiceId.HasValue && ids.Contains(x.PurchaseInvoiceId.Value)
            && x.Status == VendorReturnStatus.Posted && x.Date <= end && (!x.GlBatchId.HasValue || db.GLBatches.Any(b => b.Id == x.GlBatchId && b.CompanyId == companyId && b.Status == BatchStatus.Posted))).ToListAsync();
        decimal billedTotal = 0, creditsTotal = 0, settledTotal = 0;
        foreach (var group in bills.GroupBy(x => x.VendorId))
        {
            var groupIds = group.Select(x => x.Id).ToHashSet(); var gross = group.Sum(x => x.TotalAmount);
            var credits = debitNotes.Where(x => x.PurchaseInvoiceId.HasValue && groupIds.Contains(x.PurchaseInvoiceId.Value)).Sum(x => ReportAccounting.Money(x.TotalAmount * x.ExchangeRate));
            // Legacy debit notes reference an invoice's migrated order, not the original PO.
            credits += debitNotes.Where(x => !x.PurchaseInvoiceId.HasValue && x.PurchaseOrderId.HasValue && group.Any(b => b.PurchaseInvoiceNumber.StartsWith("PINV-LEGACY-") && b.PurchaseOrderId == x.PurchaseOrderId)).Sum(x => ReportAccounting.Money(x.TotalAmount * x.ExchangeRate));
            var groupReturns = returns.Where(x => groupIds.Contains(x.PurchaseInvoiceId!.Value)).ToList();
            credits += groupReturns.Where(x => x.ReturnType != VendorReturnType.PaymentOnly).Sum(x => ReportAccounting.Money(x.Lines.Sum(l => l.Quantity * l.UnitCost) * x.ExchangeRate));
            var refunds = groupReturns.Where(x => x.ReturnType != VendorReturnType.QuantityOnly).Sum(x => ReportAccounting.Money(x.TotalAmount * x.ExchangeRate));
            var settled = payments.Where(x => groupIds.Contains(x.PurchaseInvoiceId)).Sum(x => x.Amount + x.WithholdingAmount) - refunds;
            report.Rows.Add(new() { group.First().Vendor?.Name ?? "Unknown", group.Count().ToString(), ReportAmount(gross), ReportAmount(credits), ReportAmount(settled), ReportAmount(gross - credits - settled) });
            billedTotal += gross; creditsTotal += credits; settledTotal += settled;
        }
        report.Rows.Add(new() { "GRAND TOTAL", bills.Count.ToString(), ReportAmount(billedTotal), ReportAmount(creditsTotal), ReportAmount(settledTotal), ReportAmount(billedTotal - creditsTotal - settledTotal) });
        report.ReportingPeriod += " · Invoices dated in this period; settlements/credits through the end date";
        return report;
    }

    public async Task<StandardReportData> GenerateDiscountReceivedSummaryReportAsync(Guid companyId, DateOnly start, DateOnly end)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var report = PurchaseReport("Purchase Invoice Discount Summary", start, end, "Invoice Number", "Vendor", "Invoice Date", "Gross Value", "Discount Amount", "Net Value (excl. tax)");
        var from = start.ToDateTime(TimeOnly.MinValue); var to = end.ToDateTime(TimeOnly.MaxValue);
        var bills = await ReportAccounting.PostedBills(db, companyId).Include(x => x.Lines).Include(x => x.Vendor).Where(x => x.BillDate >= from && x.BillDate <= to).ToListAsync();
        decimal grossTotal = 0, discountTotal = 0;
        foreach (var bill in bills)
        {
            var gross = bill.Lines.Sum(x => x.LineTotal);
            var discount = ReportAccounting.Money(bill.DiscountPercentage > 0 ? gross * bill.DiscountPercentage / 100 : bill.DiscountAmount);
            if (discount == 0) continue;
            var grossBase = ReportAccounting.Money(gross * bill.ExchangeRate); var discountBase = ReportAccounting.Money(discount * bill.ExchangeRate);
            report.Rows.Add(new() { bill.PurchaseInvoiceNumber, bill.Vendor?.Name ?? "Unknown", bill.BillDate.ToString("yyyy-MM-dd"), ReportAmount(grossBase), ReportAmount(discountBase), ReportAmount(grossBase - discountBase) });
            grossTotal += grossBase; discountTotal += discountBase;
        }
        report.Rows.Add(new() { "", "GRAND TOTAL", "", ReportAmount(grossTotal), ReportAmount(discountTotal), ReportAmount(grossTotal - discountTotal) });
        return report;
    }

    public async Task<StandardReportData> GenerateThreeWayMatchExceptionReportAsync(Guid companyId, DateOnly start, DateOnly end)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var report = PurchaseReport("Three-Way Match Exceptions", start, end, "Bill Date", "Invoice Number", "Vendor", "Linked POs", "Bill Amount", "Variance Reason");
        var from = start.ToDateTime(TimeOnly.MinValue); var to = end.ToDateTime(TimeOnly.MaxValue);
        var bills = await db.PurchaseInvoices.AsNoTracking().Include(x => x.Vendor).Include(x => x.Lines).ThenInclude(x => x.PurchaseOrderLine)
            .Where(x => x.CompanyId == companyId && !x.IsDirectBill && x.BillDate >= from && x.BillDate <= to).ToListAsync();
        var orders = await db.PurchaseOrders.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id);
        var receiptLines = await ReportAccounting.PostedReceiptLines(db, companyId).ToDictionaryAsync(x => x.Id);
        foreach (var bill in bills)
        {
            var reasons = new List<string>();
            foreach (var line in bill.Lines.Where(x => x.PurchaseOrderLineId.HasValue || x.GoodsReceiptLineId.HasValue))
            {
                var poLine = line.PurchaseOrderLine;
                if (poLine == null || !orders.TryGetValue(poLine.PurchaseOrderId, out var po)) { reasons.Add("Source PO line is unresolved"); continue; }
                if (!line.GoodsReceiptLineId.HasValue || !receiptLines.TryGetValue(line.GoodsReceiptLineId.Value, out var receipt)) { reasons.Add($"{po.OrderNumber}: receipt is not posted"); continue; }
                if (Math.Abs(line.UnitCostBilled - poLine.UnitCost) > 0.0001m) reasons.Add($"{po.OrderNumber}: invoice unit cost differs from PO");
                if (line.QuantityBilled > receipt.QuantityReceived + UomConversion.QuantityTolerance) reasons.Add($"{po.OrderNumber}: billed quantity exceeds receipt quantity");
            }
            if (bill.MatchStatus == BillMatchStatus.Variance && !string.IsNullOrWhiteSpace(bill.MatchVarianceReason)) reasons.Add(bill.MatchVarianceReason);
            if (reasons.Count == 0) continue;
            var poIds = bill.Lines.Where(x => x.PurchaseOrderLine != null).Select(x => x.PurchaseOrderLine!.PurchaseOrderId).ToList();
            if (bill.PurchaseOrderId.HasValue) poIds.Add(bill.PurchaseOrderId.Value);
            var numbers = poIds.Distinct().Where(orders.ContainsKey).Select(x => orders[x].OrderNumber);
            report.Rows.Add(new() { bill.BillDate.ToString("yyyy-MM-dd"), bill.PurchaseInvoiceNumber, bill.Vendor?.Name ?? "Unknown", string.Join(", ", numbers), ReportAmount(bill.TotalAmount), string.Join("; ", reasons.Distinct()) });
        }
        return report;
    }
}
