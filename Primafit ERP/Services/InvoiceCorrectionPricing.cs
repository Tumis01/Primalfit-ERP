using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services;

internal sealed record InvoiceCorrectionPrice(decimal DiscountRate, decimal TaxRate, Guid? TaxAccountId)
{
    public decimal Gross { get; init; }
    public decimal Discount { get; init; }
    public decimal Tax { get; init; }
    public decimal GrossBase { get; init; }
    public decimal DiscountBase { get; init; }
    public decimal TaxBase { get; init; }

    public InvoiceCorrectionPrice ForQuantity(decimal prior, decimal quantity, decimal invoiced) => this with {
        Gross = InvoiceReversalMath.Portion(Gross, prior, quantity, invoiced),
        Discount = InvoiceReversalMath.Portion(Discount, prior, quantity, invoiced),
        Tax = InvoiceReversalMath.Portion(Tax, prior, quantity, invoiced),
        GrossBase = InvoiceReversalMath.Portion(GrossBase, prior, quantity, invoiced),
        DiscountBase = InvoiceReversalMath.Portion(DiscountBase, prior, quantity, invoiced),
        TaxBase = InvoiceReversalMath.Portion(TaxBase, prior, quantity, invoiced)
    };
}

// Read the tax actually posted on the source invoice. A later change to the
// tax master must not change the value of a reversal of an older invoice.
internal static class InvoiceCorrectionPricing
{
    public static async Task<Dictionary<Guid, InvoiceCorrectionPrice>> SalesAsync(AppDbContext db, SalesInvoice invoice)
    {
        var gross = invoice.Lines.Sum(x => x.Amount);
        var discount = invoice.DiscountPercentage > 0 ? gross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, gross);
        var discountRate = gross > 0 ? discount / gross : 0;
        var discountForeign = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        var taxForeign = Math.Max(0, invoice.TotalAmountForeign - gross + discountForeign);
        if (invoice.TotalAmountForeign > 0 && !invoice.Lines.Any(x => (x.TaxId ?? invoice.TaxId).HasValue)) discountForeign = gross - invoice.TotalAmountForeign;
        var result = Allocate(invoice.Lines.Select(x => (x.Id, x.Amount)).ToList(), discountForeign, discountRate, invoice.ExchangeRate);
        if (!invoice.Lines.Any(x => (x.TaxId ?? invoice.TaxId).HasValue)) return result;
        var ledger = await db.GLTransactions.AsNoTracking().Where(x => x.CompanyId == invoice.CompanyId && x.BatchId == invoice.GLBatchId).ToListAsync();
        var groups = new List<(List<SalesInvoiceLine> Lines, decimal BaseTax, Guid Account)>();
        foreach (var group in invoice.Lines.GroupBy(x => x.TaxId ?? invoice.TaxId).Where(x => x.Key.HasValue))
        {
            var tax = await db.Taxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == group.Key && x.CompanyId == invoice.CompanyId);
            if (tax == null) throw new InvalidOperationException("The source invoice tax record is unavailable.");
            var entries = ledger.Where(x => x.Credit > 0 && x.Narration == $"{tax.TaxCode} {invoice.InvoiceNumber}").ToList();
            if (entries.Count == 0)
            {
                var originalAccount = group.Select(x => x.TaxGLAccountId).FirstOrDefault(x => x.HasValue) ?? invoice.TaxGLAccountId;
                var sameAccountGroups = invoice.Lines.GroupBy(x => x.TaxId ?? invoice.TaxId)
                    .Where(x => x.Key.HasValue && (x.Select(y => y.TaxGLAccountId).FirstOrDefault(y => y.HasValue) ?? invoice.TaxGLAccountId) == originalAccount).Count();
                if (originalAccount.HasValue && sameAccountGroups == 1)
                    entries = ledger.Where(x => x.Credit > 0 && x.SegCoaId == originalAccount.Value
                        && x.Narration != null && x.Narration.EndsWith(" " + invoice.InvoiceNumber)
                        && !x.Narration.StartsWith("Sales ") && !x.Narration.StartsWith("Discount ")).ToList();
            }
            if (entries.Count == 0) continue; // A zero-rated group has no posted tax entry.
            var accounts = entries.Select(x => x.SegCoaId).Distinct().ToList();
            if (accounts.Count != 1) throw new InvalidOperationException("The original invoice tax account could not be verified.");
            groups.Add((group.ToList(), entries.Sum(x => x.Credit), accounts[0]));
        }
        var taxBase = groups.Sum(x => x.BaseTax);
        if (taxBase <= 0)
        {
            if (taxForeign <= 0.01m) return result; // Invoice rounding on a zero-rated sale.
            throw new InvalidOperationException("The original invoice tax amount could not be verified.");
        }
        decimal previousTaxBase = 0;
        foreach (var group in groups)
        {
            var groupGross = group.Lines.Sum(x => x.Amount);
            var taxRate = groupGross > 0 ? taxForeign * group.BaseTax / taxBase / groupGross : 0;
            var groupTaxForeign = Share(taxForeign, previousTaxBase, group.BaseTax, taxBase);
            decimal cumulative = 0;
            foreach (var line in group.Lines.OrderBy(x => x.Id))
            {
                result[line.Id] = result[line.Id] with { TaxRate = taxRate, TaxAccountId = group.Account,
                    Tax = Share(groupTaxForeign, cumulative, line.Amount, groupGross),
                    TaxBase = Share(group.BaseTax, cumulative, line.Amount, groupGross) };
                cumulative += line.Amount;
            }
            previousTaxBase += group.BaseTax;
        }
        return result;
    }

    public static async Task<Dictionary<Guid, InvoiceCorrectionPrice>> PurchaseAsync(AppDbContext db, PurchaseInvoice invoice)
    {
        var gross = invoice.Lines.Sum(x => x.LineTotal);
        var discount = invoice.DiscountPercentage > 0 ? gross * invoice.DiscountPercentage / 100m : Math.Min(invoice.DiscountAmount, gross);
        var discountForeign = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        var tax = invoice.TaxId.HasValue ? Math.Max(0, invoice.TotalAmountForeign - gross + discountForeign) : 0;
        if (invoice.TotalAmountForeign > 0 && !invoice.TaxId.HasValue) discountForeign = gross - invoice.TotalAmountForeign;
        Guid? taxAccount = null;
        decimal taxBase = 0;
        if (invoice.TaxId.HasValue)
        {
            var accounts = await db.GLTransactions.AsNoTracking().Where(x => x.CompanyId == invoice.CompanyId && x.BatchId == invoice.GLBatchId
                && x.Debit > 0 && x.Narration == $"Input Tax: {invoice.PurchaseInvoiceNumber}").Select(x => x.SegCoaId).Distinct().ToListAsync();
            if (accounts.Count == 0 && tax <= 0.01m) tax = 0;
            else if (accounts.Count != 1) throw new InvalidOperationException("The original invoice tax account could not be verified.");
            else taxAccount = accounts[0];
            if (taxAccount.HasValue) taxBase = await db.GLTransactions.Where(x => x.CompanyId == invoice.CompanyId && x.BatchId == invoice.GLBatchId
                && x.Debit > 0 && x.Narration == $"Input Tax: {invoice.PurchaseInvoiceNumber}").SumAsync(x => x.Debit);
        }
        var result = Allocate(invoice.Lines.Select(x => (x.Id, x.LineTotal)).ToList(), discountForeign, gross > 0 ? discount / gross : 0, invoice.ExchangeRate);
        decimal cumulative = 0;
        foreach (var line in invoice.Lines.OrderBy(x => x.Id))
        {
            result[line.Id] = result[line.Id] with { TaxRate = gross > 0 ? tax / gross : 0, TaxAccountId = taxAccount,
                Tax = Share(tax, cumulative, line.LineTotal, gross), TaxBase = Share(taxBase, cumulative, line.LineTotal, gross) };
            cumulative += line.LineTotal;
        }
        return result;
    }

    private static decimal Share(decimal amount, decimal previous, decimal value, decimal total) => InvoiceReversalMath.Portion(amount, previous, value, total);

    private static Dictionary<Guid, InvoiceCorrectionPrice> Allocate(List<(Guid Id, decimal Gross)> lines, decimal discountForeign, decimal discountRate, decimal rate)
    {
        var gross = lines.Sum(x => x.Gross);
        var discountBase = Math.Round(gross * discountRate * rate, 2, MidpointRounding.AwayFromZero);
        var result = new Dictionary<Guid, InvoiceCorrectionPrice>();
        decimal previous = 0;
        foreach (var line in lines.OrderBy(x => x.Id))
        {
            result[line.Id] = new(discountRate, 0, null) { Gross = line.Gross, GrossBase = Math.Round(line.Gross * rate, 2, MidpointRounding.AwayFromZero),
                Discount = Share(discountForeign, previous, line.Gross, gross), DiscountBase = Share(discountBase, previous, line.Gross, gross) };
            previous += line.Gross;
        }
        return result;
    }
}
