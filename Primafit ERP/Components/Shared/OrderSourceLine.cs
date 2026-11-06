using Primafit_ERP.Components.Models;
namespace Primafit_ERP.Components.Shared;

public sealed record OrderSourceLine(Guid Id, Guid DocumentId, string DocumentNumber, string ItemName, Guid? ItemId,
    string? Description, Guid? UomId, string UomName, decimal UomConversionFactor, decimal Quantity, decimal UnitPrice,
    decimal ExchangeRate, Guid? TaxId, decimal DiscountPercentage, decimal DiscountAmount, Guid? WithholdingTaxId, Guid? DimensionSetId = null);
