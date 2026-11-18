using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Components.Models.Reporting;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

public enum FulfilmentReportKind { SalesInvoicedNotShipped, SalesShippedNotInvoiced, PurchaseInvoicedNotReceived, PurchaseReceivedNotInvoiced }

public sealed class FulfilmentReportService(IDbContextFactory<AppDbContext> factory)
{
    private sealed record Line(Guid Id, string Document, DateOnly Date, Guid PartnerId, Guid ItemId,
        Guid? OrderLineId, Guid? LinkedLineId, decimal Quantity, decimal? UnitValue, string RelatedDocument);

    public static string Title(FulfilmentReportKind kind) => kind switch {
        FulfilmentReportKind.SalesInvoicedNotShipped => "Sales invoices not shipped",
        FulfilmentReportKind.SalesShippedNotInvoiced => "Shipments not invoiced",
        FulfilmentReportKind.PurchaseInvoicedNotReceived => "Purchase invoices not received",
        _ => "Goods receipts not invoiced"
    };

    public async Task<StandardReportData> GenerateAsync(Guid companyId, FulfilmentReportKind kind, DateOnly asOf,
        Guid? partnerId = null, string itemSearch = "")
    {
        if (companyId == Guid.Empty || !Enum.IsDefined(kind)) throw new ArgumentException("Select a company and a valid report.");
        await using var db = await factory.CreateDbContextAsync();
        // The accounting date is authoritative. Draft/rejected/ready batches never count.
        var posted = await db.GLBatches.AsNoTracking().Where(b => b.CompanyId == companyId && b.Status == BatchStatus.Posted
            && b.Journals.Any(h => h.TransactionDate <= asOf) && !b.Journals.Any(h => h.TransactionDate > asOf))
            .Select(b => b.Id).ToListAsync();
        var sales = kind is FulfilmentReportKind.SalesInvoicedNotShipped or FulfilmentReportKind.SalesShippedNotInvoiced;
        var (invoices, movements) = sales
            ? await SalesAsync(db, companyId, asOf, posted)
            : await PurchasesAsync(db, companyId, asOf, posted);
        var invoiceMatched = invoices.ToDictionary(l => l.Id, _ => 0m);
        var movementMatched = movements.ToDictionary(l => l.Id, _ => 0m);
        var invoiceById = invoices.ToDictionary(l => l.Id);
        var movementById = movements.ToDictionary(l => l.Id);

        void Match(Line invoice, Line movement)
        {
            if (invoice.PartnerId != movement.PartnerId || invoice.ItemId != movement.ItemId) return;
            var quantity = Math.Min(invoice.Quantity - invoiceMatched[invoice.Id], movement.Quantity - movementMatched[movement.Id]);
            if (quantity <= 0) return;
            invoiceMatched[invoice.Id] += quantity; movementMatched[movement.Id] += quantity;
        }
        // Allocate explicit source links first, in either workflow direction.
        foreach (var invoice in invoices)
            if (invoice.LinkedLineId is Guid movementId && movementById.TryGetValue(movementId, out var movement)) Match(invoice, movement);
        foreach (var movement in movements)
            if (movement.LinkedLineId is Guid invoiceId && invoiceById.TryGetValue(invoiceId, out var invoice)) Match(invoice, movement);
        // Older order-sourced documents may have no direct cross-link. Consume their
        // remaining quantities once, in document order, rather than counting a receipt
        // against every partial invoice for the same order line.
        var byOrder = movements.Where(l => l.OrderLineId.HasValue).GroupBy(l => l.OrderLineId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var invoice in invoices.Where(l => l.OrderLineId.HasValue))
        {
            if (!byOrder.TryGetValue(invoice.OrderLineId!.Value, out var candidates)) continue;
            foreach (var movement in candidates.Where(m => !m.LinkedLineId.HasValue || m.LinkedLineId == invoice.Id))
            {
                if (invoice.LinkedLineId.HasValue && invoice.LinkedLineId != movement.Id
                    && (!movementById.TryGetValue(invoice.LinkedLineId.Value, out var original)
                        || movementMatched[original.Id] + UomConversion.QuantityTolerance < original.Quantity)) continue;
                Match(invoice, movement);
            }
        }

        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id);
        var partners = sales ? await db.Customers.Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, p => p.Name)
            : await db.Vendors.Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, p => p.Name);
        bool Include(Guid partner, Guid item) => (!partnerId.HasValue || partner == partnerId)
            && (string.IsNullOrWhiteSpace(itemSearch) || items.TryGetValue(item, out var master)
                && (master.Name.Contains(itemSearch.Trim(), StringComparison.OrdinalIgnoreCase) || master.Code.Contains(itemSearch.Trim(), StringComparison.OrdinalIgnoreCase)));
        var invoiceReport = kind is FulfilmentReportKind.SalesInvoicedNotShipped or FulfilmentReportKind.PurchaseInvoicedNotReceived;
        var source = invoiceReport ? invoices : movements;
        var matches = invoiceReport ? invoiceMatched : movementMatched;
        var data = new StandardReportData {
            ReportName = Title(kind), ReportingPeriod = $"As of {asOf:dd MMM yyyy} · GL-posted documents only · Values exclude tax and discounts.",
            Currency = await db.Currencies.Where(c => c.CompanyId == companyId && c.IsBaseCurrency).Select(c => c.CurrencyCode).FirstOrDefaultAsync() ?? "",
            Headers = new() { "Entry", "Document", "Date", sales ? "Customer" : "Vendor", "Item", "Primary UOM", "Document qty", invoiceReport ? (sales ? "Shipped qty" : "Received qty") : "Invoiced qty", "Outstanding qty", "Outstanding value (base)", "Related document" }
        };
        data.Columns = Enumerable.Repeat(new ReportColumn(ReportColumnKind.Text), data.Headers.Count).ToList();
        data.Columns[2] = new(ReportColumnKind.Date);
        foreach (var index in new[] { 6, 7, 8 }) data.Columns[index] = new(ReportColumnKind.Number, 4);
        data.Columns[9] = new(ReportColumnKind.Number, 2);
        decimal totalOutstanding = 0;
        foreach (var line in source.Where(l => Include(l.PartnerId, l.ItemId)))
        {
            var outstanding = Math.Max(0, line.Quantity - matches[line.Id]);
            if (outstanding <= UomConversion.QuantityTolerance) continue;
            var value = line.UnitValue.HasValue ? ReportAccounting.Money(outstanding * line.UnitValue.Value) : (decimal?)null;
            totalOutstanding += value ?? 0;
            data.Rows.Add(new() { "Outstanding", line.Document, line.Date.ToString("yyyy-MM-dd"), partners.GetValueOrDefault(line.PartnerId, "Source unavailable"),
                items.GetValueOrDefault(line.ItemId)?.Name ?? "Item unavailable", items.GetValueOrDefault(line.ItemId)?.UoM ?? "",
                N(line.Quantity), N(matches[line.Id]), N(outstanding), value?.ToString("N2") ?? "", line.RelatedDocument });
        }
        if (data.Rows.Count > 0)
            data.Rows.Add(new() { "Total", "", "", "", "", "", "", "", "", totalOutstanding.ToString("N2"), "" });
        return data;
    }

    private static string N(decimal number) => number.ToString("N4");
    private static List<Line> Ordered(List<Line> lines) => lines.OrderBy(l => l.Date).ThenBy(l => l.Document, StringComparer.Ordinal).ThenBy(l => l.Id).ToList();

    private static async Task<(List<Line>, List<Line>)> SalesAsync(AppDbContext db, Guid company, DateOnly asOf, List<Guid> posted)
    {
        var documents = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == company && !i.IsDirectInvoice && i.Status != SalesInvoiceStatus.Cancelled
                && i.Date <= asOf && i.GLBatchId.HasValue && posted.Contains(i.GLBatchId.Value)).ToListAsync();
        var orders = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == company).ToListAsync();
        var orderLines = orders.SelectMany(o => o.Lines.Select(l => (l.Id, Line: l, Order: o))).ToDictionary(x => x.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == company && !i.IsService).Select(i => i.Id).ToHashSetAsync();
        var invoices = documents.SelectMany(i => i.Lines.Where(l => l.ItemId.HasValue && items.Contains(l.ItemId.Value) && l.QuantityInvoiced > 0)
            .Select(l => new Line(l.Id, i.InvoiceNumber, i.Date, i.CustomerId, l.ItemId!.Value, l.SalesOrderLineId, l.ShipmentLineId,
                l.QuantityInvoiced, l.UnitPrice * i.ExchangeRate, l.SalesOrderLineId.HasValue && orderLines.TryGetValue(l.SalesOrderLineId.Value, out var root) ? root.Order.OrderNumber : ""))).ToList();
        var shipments = await db.SalesShipments.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.CompanyId == company && s.Status != ShipmentStatus.Cancelled && s.ShipmentBatchId.HasValue && posted.Contains(s.ShipmentBatchId.Value)).ToListAsync();
        var sourceIds = shipments.SelectMany(s => s.Lines).Where(l => l.SalesInvoiceLineId.HasValue).Select(l => l.SalesInvoiceLineId!.Value).Distinct().ToList();
        var invoiceSources = await db.SalesInvoiceLines.AsNoTracking().Include(l => l.SalesInvoice)
            .Where(l => sourceIds.Contains(l.Id) && l.SalesInvoice!.CompanyId == company).ToDictionaryAsync(l => l.Id);
        var movements = new List<Line>();
        foreach (var shipment in shipments)
        foreach (var line in shipment.Lines.Where(l => l.QtyShipped > 0 && items.Contains(l.ItemId)))
        {
            var date = DateOnly.FromDateTime(shipment.ShippedDate ?? shipment.CreatedDate);
            if (date > asOf) continue;
            var root = line.SalesOrderLineId.HasValue ? orderLines.GetValueOrDefault(line.SalesOrderLineId.Value) : default;
            // Read invoice ownership even while its invoice batch is awaiting approval.
            var invoiceSource = line.SalesInvoiceLineId.HasValue ? invoiceSources.GetValueOrDefault(line.SalesInvoiceLineId.Value) : null;
            var partner = root.Order?.CustomerId ?? invoiceSource?.SalesInvoice?.CustomerId ?? Guid.Empty;
            var unitValue = invoiceSource != null ? invoiceSource.UnitPrice * invoiceSource.SalesInvoice!.ExchangeRate
                : root.Line != null ? root.Line.UnitPrice * root.Order.ExchangeRate : (decimal?)null;
            movements.Add(new(line.Id, shipment.ShipmentNumber, date, partner, line.ItemId, line.SalesOrderLineId,
                line.SalesInvoiceLineId, line.QtyShipped, unitValue, root.Order?.OrderNumber ?? invoiceSource?.SalesInvoice?.InvoiceNumber ?? ""));
        }
        return (Ordered(invoices), Ordered(movements));
    }

    private static async Task<(List<Line>, List<Line>)> PurchasesAsync(AppDbContext db, Guid company, DateOnly asOf, List<Guid> posted)
    {
        var documents = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.CompanyId == company && !i.IsDirectBill && i.InvoiceDate.Date <= asOf.ToDateTime(TimeOnly.MinValue)
                && i.GLBatchId.HasValue && posted.Contains(i.GLBatchId.Value)).ToListAsync();
        var orders = await db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == company).ToListAsync();
        var orderLines = orders.SelectMany(o => o.Lines.Select(l => (l.Id, Line: l, Order: o))).ToDictionary(x => x.Id);
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == company && !i.IsService).Select(i => i.Id).ToHashSetAsync();
        var invoices = documents.SelectMany(i => i.Lines.Where(l => l.ItemId.HasValue && items.Contains(l.ItemId.Value) && l.QuantityBilled > 0)
            .Select(l => new Line(l.Id, i.PurchaseInvoiceNumber, DateOnly.FromDateTime(i.InvoiceDate), i.VendorId, l.ItemId!.Value,
                l.PurchaseOrderLineId, l.GoodsReceiptLineId, l.QuantityBilled, l.UnitCostBilled * i.ExchangeRate,
                l.PurchaseOrderLineId.HasValue && orderLines.TryGetValue(l.PurchaseOrderLineId.Value, out var root) ? root.Order.OrderNumber : ""))).ToList();
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CompanyId == company && r.DateReceived.Date <= asOf.ToDateTime(TimeOnly.MinValue)
                && r.GLBatchId.HasValue && posted.Contains(r.GLBatchId.Value)).ToListAsync();
        var sourceIds = receipts.SelectMany(r => r.Lines).Where(l => l.PurchaseInvoiceLineId.HasValue).Select(l => l.PurchaseInvoiceLineId!.Value).Distinct().ToList();
        var invoiceSources = await db.PurchaseInvoiceLines.AsNoTracking().Include(l => l.PurchaseInvoice)
            .Where(l => sourceIds.Contains(l.Id) && l.PurchaseInvoice!.CompanyId == company).ToDictionaryAsync(l => l.Id);
        var movements = new List<Line>();
        foreach (var receipt in receipts)
        foreach (var line in receipt.Lines.Where(l => l.QuantityReceived > 0))
        {
            var root = line.PurchaseOrderLineId.HasValue ? orderLines.GetValueOrDefault(line.PurchaseOrderLineId.Value) : default;
            var invoice = line.PurchaseInvoiceLineId.HasValue ? invoiceSources.GetValueOrDefault(line.PurchaseInvoiceLineId.Value) : null;
            var item = root.Line?.ItemId ?? invoice?.ItemId ?? Guid.Empty;
            if (!items.Contains(item)) continue;
            movements.Add(new(line.Id, receipt.GrnNumber, DateOnly.FromDateTime(receipt.DateReceived), root.Order?.VendorId ?? invoice?.PurchaseInvoice?.VendorId ?? Guid.Empty,
                item, line.PurchaseOrderLineId, line.PurchaseInvoiceLineId, line.QuantityReceived,
                invoice != null ? invoice.UnitCostBilled * invoice.PurchaseInvoice!.ExchangeRate : root.Line != null ? root.Line.UnitCost * root.Order.ExchangeRate : null,
                root.Order?.OrderNumber ?? invoice?.PurchaseInvoice?.PurchaseInvoiceNumber ?? ""));
        }
        return (Ordered(invoices), Ordered(movements));
    }
}
