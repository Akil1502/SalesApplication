namespace SalesApp.Core.Purchasing;

/// <summary>
/// Weighted average cost. A receipt blends its price into the cost of what's already on the shelf; a bill then adds
/// what the receipt couldn't know (a different price on the bill, freight and other charges), but only for the part of
/// that delivery still in stock: the part already sold has gone at the old cost. Costs are rounded to the paisa.
/// </summary>
public static class PurchaseCosting
{
    /// <summary>
    /// The cost after receiving <paramref name="quantity"/> at <paramref name="unitCost"/> on top of
    /// <paramref name="onHandBefore"/> at <paramref name="currentCost"/>. With nothing on the shelf (or no cost yet)
    /// the new goods set the cost: 100 kg at ₹200 plus 50 kg at ₹230 gives ₹210.
    /// </summary>
    public static decimal AfterReceipt(decimal? currentCost, decimal onHandBefore, decimal quantity, decimal unitCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (currentCost is not { } cost || onHandBefore <= 0)
        {
            return Money(unitCost);
        }

        return Money((onHandBefore * cost + quantity * unitCost) / (onHandBefore + quantity));
    }

    /// <summary>
    /// The cost after a bill adds <paramref name="extra"/> rupees (positive or negative) to a delivery of
    /// <paramref name="billedQuantity"/> units. Only the share still on hand changes the cost, spread over everything
    /// on hand. Nothing changes when the product has no cost yet or no stock left.
    /// </summary>
    public static decimal? AfterBillAdjustment(decimal? currentCost, decimal onHand, decimal billedQuantity, decimal extra)
    {
        if (currentCost is not { } cost || onHand <= 0 || billedQuantity <= 0 || extra == 0)
        {
            return currentCost;
        }

        var stillHere = Math.Min(onHand, billedQuantity);
        var share = extra * stillHere / billedQuantity;
        return Money(Math.Max(0m, cost + share / onHand));
    }

    /// <summary>
    /// Splits <paramref name="amount"/> in proportion to <paramref name="weights"/> (freight by line value), to the
    /// paisa; the largest line takes the rounding difference so the parts always add up to the amount.
    /// </summary>
    public static IReadOnlyList<decimal> Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var total = weights.Sum();
        if (weights.Count == 0)
        {
            return [];
        }

        if (total <= 0)
        {
            // Nothing to weigh by: split evenly.
            return Allocate(amount, [.. weights.Select(_ => 1m)]);
        }

        var parts = weights.Select(w => Money(amount * w / total)).ToArray();
        var largest = 0;
        for (var i = 1; i < weights.Count; i++)
        {
            if (weights[i] > weights[largest])
            {
                largest = i;
            }
        }

        parts[largest] += amount - parts.Sum();
        return parts;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
