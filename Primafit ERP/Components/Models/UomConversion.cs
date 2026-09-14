namespace Primafit_ERP.Components.Models;

public static class UomConversion
{
    public static decimal NormalizeFactor(decimal factor) => factor > 0 ? factor : 1m;

    public static decimal ToBase(decimal selectedQuantity, decimal selectedToBaseFactor) =>
        selectedQuantity * NormalizeFactor(selectedToBaseFactor);

    public static decimal FromBase(decimal baseQuantity, decimal selectedToBaseFactor) =>
        baseQuantity / NormalizeFactor(selectedToBaseFactor);

    public static decimal PriceFromBase(decimal baseUnitPrice, decimal selectedToBaseFactor) =>
        baseUnitPrice * NormalizeFactor(selectedToBaseFactor);

    public static decimal PriceToBase(decimal selectedUnitPrice, decimal selectedToBaseFactor) =>
        selectedUnitPrice / NormalizeFactor(selectedToBaseFactor);

    public static decimal FactorFor(Item item, Guid? selectedUomId)
    {
        if (item.AlternateUomId.HasValue && item.AlternateUomId == selectedUomId)
            return 1m;

        if (item.UomId.HasValue && item.UomId == selectedUomId)
            return NormalizeFactor(item.AlternateUomConversionFactor);

        return 1m;
    }

    public static string NameFor(Item item, Guid? selectedUomId)
    {
        if (item.AlternateUomId.HasValue && item.AlternateUomId == selectedUomId)
            return item.AlternateUom?.Name ?? item.UoM;

        if (item.UomId.HasValue && item.UomId == selectedUomId)
            return item.PrimaryUom?.Name ?? item.UoM;

        return item.UoM;
    }
}
