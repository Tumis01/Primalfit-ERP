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
        => FactorFor(item, selectedUomId, null);

    public static decimal FactorFor(Item item, Guid? selectedUomId,
        IReadOnlyDictionary<(Guid From, Guid To), decimal>? rules)
    {
        if (!selectedUomId.HasValue || selectedUomId == item.UomId)
            return 1m;

        if (item.UomId.HasValue && rules != null)
        {
            // Transaction factors are selected-UOM -> primary-UOM. Global
            // rules are stored in the opposite, human-readable direction.
            var primaryToSelected = FactorBetween(item.UomId.Value, selectedUomId.Value, rules);
            if (primaryToSelected.HasValue) return 1m / NormalizeFactor(primaryToSelected.Value);
        }

        return 1m;
    }

    public static decimal FactorFor(Item item, Guid? selectedUomId,
        IReadOnlyDictionary<Guid, decimal>? itemUomFactors,
        IReadOnlyDictionary<(Guid From, Guid To), decimal>? rules = null)
    {
        if (selectedUomId.HasValue && itemUomFactors != null && itemUomFactors.TryGetValue(selectedUomId.Value, out var itemFactor))
            return 1m / NormalizeFactor(itemFactor);
        return FactorFor(item, selectedUomId, rules);
    }

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
