namespace Primafit_ERP.Services;

internal static class PurchaseInvoiceReference
{
    public static string Resolve(string automaticNumber, string? supplied = null,
        string? existingSupplierReference = null, string? existingExternalReference = null) =>
        new[] { supplied, existingSupplierReference, existingExternalReference, automaticNumber }
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
