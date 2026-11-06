using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

// Source lines are consumed in full by an order even if the copied order line
// is subsequently changed. The unique source-line indexes arbitrate races.
internal static class OrderSourceValidation
{
    internal static async Task<string?> PrepareSalesAsync(AppDbContext db, SalesOrder order)
    {
        var ids = order.Lines.Where(x => x.SalesQuoteLineId.HasValue).Select(x => x.SalesQuoteLineId!.Value).ToList();
        if (ids.Count != ids.Distinct().Count()) return "A quote line can only be added once.";
        if (ids.Count == 0) return null;
        var lines = await db.SalesQuoteLines.Include(x => x.SalesQuote).Where(x => ids.Contains(x.Id)).ToListAsync();
        if (lines.Count != ids.Count || lines.Any(x => x.SalesQuote == null || x.SalesQuote.CompanyId != order.CompanyId || x.SalesQuote.CustomerId != order.CustomerId || x.SalesQuote.CurrencyId != order.CurrencyId)) return "Every source quote must match this company, customer and currency.";
        var usedElsewhere = await db.SalesOrderLines.Where(x => x.HeaderId != order.Id && x.SalesQuoteLineId.HasValue && ids.Contains(x.SalesQuoteLineId.Value)).Select(x => x.SalesQuoteLineId!.Value).ToListAsync();
        if (usedElsewhere.Count > 0) return "A selected quote line has already been used by another order.";
        var previouslyUsed = await db.SalesOrderLines.Where(x => x.HeaderId == order.Id && x.SalesQuoteLineId.HasValue).Select(x => x.SalesQuoteLineId!.Value).ToListAsync();
        if (lines.Any(x => !previouslyUsed.Contains(x.Id) && (x.SalesQuote!.Status is not (SalesQuoteStatus.Open or SalesQuoteStatus.PartiallyConverted) || (x.SalesQuote.ValidUntil.HasValue && x.SalesQuote.ValidUntil < DateOnly.FromDateTime(DateTime.Today))))) return "A selected quote is no longer available for conversion.";
        foreach (var quote in lines.Select(x => x.SalesQuote!).DistinctBy(x => x.Id))
        {
            var allIds = await db.SalesQuoteLines.Where(x => x.SalesQuoteId == quote.Id).Select(x => x.Id).ToListAsync();
            var used = await db.SalesOrderLines.Where(x => x.SalesQuoteLineId.HasValue && allIds.Contains(x.SalesQuoteLineId.Value)).Select(x => x.SalesQuoteLineId!.Value).ToListAsync();
            quote.Status = allIds.All(x => ids.Contains(x) || used.Contains(x)) ? SalesQuoteStatus.Converted : SalesQuoteStatus.PartiallyConverted;
        }
        return null;
    }

    internal static async Task<string?> PreparePurchaseAsync(AppDbContext db, PurchaseOrder order)
    {
        var ids = order.Lines.Where(x => x.PurchaseRequestLineId.HasValue).Select(x => x.PurchaseRequestLineId!.Value).ToList();
        if (ids.Count != ids.Distinct().Count()) return "A request line can only be added once.";
        if (ids.Count == 0) return null;
        var lines = await db.PurchaseRequestLines.Include(x => x.PurchaseRequest).Where(x => ids.Contains(x.Id)).ToListAsync();
        if (lines.Count != ids.Count || lines.Any(x => x.PurchaseRequest == null || x.PurchaseRequest.CompanyId != order.CompanyId || x.PurchaseRequest.VendorId != order.VendorId || x.PurchaseRequest.CurrencyId != order.CurrencyId)) return "Every source request must match this company, supplier and currency.";
        var usedElsewhere = await db.PurchaseOrderLines.Where(x => x.PurchaseOrderId != order.Id && x.PurchaseRequestLineId.HasValue && ids.Contains(x.PurchaseRequestLineId.Value)).Select(x => x.PurchaseRequestLineId!.Value).ToListAsync();
        if (usedElsewhere.Count > 0) return "A selected request line has already been used by another order.";
        var previouslyUsed = await db.PurchaseOrderLines.Where(x => x.PurchaseOrderId == order.Id && x.PurchaseRequestLineId.HasValue).Select(x => x.PurchaseRequestLineId!.Value).ToListAsync();
        if (lines.Any(x => !previouslyUsed.Contains(x.Id) && x.PurchaseRequest!.Status is not (PurchaseRequestStatus.Open or PurchaseRequestStatus.PartiallyConverted))) return "A selected request is no longer available for conversion.";
        foreach (var request in lines.Select(x => x.PurchaseRequest!).DistinctBy(x => x.Id))
        {
            var allIds = await db.PurchaseRequestLines.Where(x => x.PurchaseRequestId == request.Id).Select(x => x.Id).ToListAsync();
            var used = await db.PurchaseOrderLines.Where(x => x.PurchaseRequestLineId.HasValue && allIds.Contains(x.PurchaseRequestLineId.Value)).Select(x => x.PurchaseRequestLineId!.Value).ToListAsync();
            request.Status = allIds.All(x => ids.Contains(x) || used.Contains(x)) ? PurchaseRequestStatus.Converted : PurchaseRequestStatus.PartiallyConverted;
        }
        return null;
    }
}
