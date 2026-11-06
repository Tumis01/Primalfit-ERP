namespace Primafit_ERP.Components.Models;

public static class UomConversion
{
    public const decimal QuantityTolerance = 0.0000000001m;
    public static decimal NormalizeFactor(decimal factor) => factor > 0 ? factor : 1m;

    public static decimal ToBase(decimal selectedQuantity, decimal selectedToBaseFactor) =>
        Math.Round(selectedQuantity * NormalizeFactor(selectedToBaseFactor), 12, MidpointRounding.AwayFromZero);

    public static decimal FromBase(decimal baseQuantity, decimal selectedToBaseFactor) =>
        Math.Round(baseQuantity / NormalizeFactor(selectedToBaseFactor), 4, MidpointRounding.AwayFromZero);

    public static decimal PriceFromBase(decimal baseUnitPrice, decimal selectedToBaseFactor) =>
        baseUnitPrice * NormalizeFactor(selectedToBaseFactor);

    public static decimal PriceToBase(decimal selectedUnitPrice, decimal selectedToBaseFactor) =>
        selectedUnitPrice / NormalizeFactor(selectedToBaseFactor);

    public static decimal FactorFor(Item item, Guid? selectedUomId)
        => FactorFor(item, selectedUomId, item.UomConversions.Where(x => x.IsActive)
            .ToDictionary(x => x.UomId, x => x.ConversionFactorToBase), null);

    public static decimal FactorFor(Item item, Guid? selectedUomId,
        IReadOnlyDictionary<(Guid From, Guid To), decimal>? rules)
        => FactorFor(item, selectedUomId);

    public static decimal FactorFor(Item item, Guid? selectedUomId,
        IReadOnlyDictionary<Guid, decimal>? itemUomFactors,
        IReadOnlyDictionary<(Guid From, Guid To), decimal>? rules = null)
    {
        if (!selectedUomId.HasValue || selectedUomId == item.UomId) return 1m;
        decimal? itemFactor = itemUomFactors != null && itemUomFactors.TryGetValue(selectedUomId.Value, out var configured)
            ? configured : item.UomConversions.FirstOrDefault(x => x.IsActive && x.UomId == selectedUomId)?.ConversionFactorToBase;
        if (!itemFactor.HasValue || itemFactor <= 0)
            throw new InvalidOperationException($"The selected UOM is not configured for {item.Name}. Review this item's equivalent UOMs.");
        // Item setup stores: 1 primary UOM = N equivalent UOMs.
        // Transaction snapshots store the inverse: selected UOM -> primary UOM.
        return Math.Round(1m / itemFactor.Value, 16, MidpointRounding.AwayFromZero);
    }

    public static bool IsConfigured(Item item, Guid? uomId, IReadOnlyDictionary<Guid, decimal>? itemFactors = null) =>
        !uomId.HasValue || uomId == item.UomId || (itemFactors?.GetValueOrDefault(uomId.Value) > 0)
        || item.UomConversions.Any(x => x.IsActive && x.UomId == uomId && x.ConversionFactorToBase > 0);

    public static IReadOnlyCollection<Guid> ConfiguredUoms(Item item) => item.UomConversions.Where(x => x.IsActive)
        .Select(x => x.UomId).Concat(item.UomId.HasValue ? new[] { item.UomId.Value } : Array.Empty<Guid>()).Distinct().ToList();

    public static bool Exceeds(decimal quantity, decimal available) => quantity - available > QuantityTolerance;
    public static decimal WithinRemaining(decimal quantity, decimal remaining) => !Exceeds(quantity, remaining) && quantity > remaining ? remaining : quantity;
    public static bool SameFactor(decimal expected, decimal actual) => Math.Abs(expected - actual) <= 0.000000000000001m;

    public static decimal? FactorBetween(Guid from, Guid to,
        IReadOnlyDictionary<(Guid From, Guid To), decimal> rules)
    {
        if (from == to) return 1m;
        var queue = new Queue<(Guid Id, decimal Factor)>();
        var visited = new HashSet<Guid> { from };
        queue.Enqueue((from, 1m));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var pair in rules)
            {
                if (pair.Key.From == current.Id && visited.Add(pair.Key.To))
                {
                    var factor = current.Factor * NormalizeFactor(pair.Value);
                    if (pair.Key.To == to) return factor;
                    queue.Enqueue((pair.Key.To, factor));
                }
                else if (pair.Key.To == current.Id && visited.Add(pair.Key.From))
                {
                    var factor = current.Factor / NormalizeFactor(pair.Value);
                    if (pair.Key.From == to) return factor;
                    queue.Enqueue((pair.Key.From, factor));
                }
            }
        }
        return null;
    }

    public static IReadOnlyCollection<Guid> ReachableUoms(Guid primaryUomId,
        IEnumerable<(Guid From, Guid To)> rules)
    {
        var reachable = new HashSet<Guid> { primaryUomId };
        var pending = new Queue<Guid>();
        pending.Enqueue(primaryUomId);
        var graph = rules.ToList();
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var rule in graph.Where(x => x.From == current || x.To == current))
            {
                var next = rule.From == current ? rule.To : rule.From;
                if (reachable.Add(next)) pending.Enqueue(next);
            }
        }
        return reachable;
    }

    public static string NameFor(Item item, Guid? selectedUomId)
    {
        if (item.UomId.HasValue && item.UomId == selectedUomId)
            return item.PrimaryUom?.Name ?? item.UoM;

        return item.UoM;
    }
}
