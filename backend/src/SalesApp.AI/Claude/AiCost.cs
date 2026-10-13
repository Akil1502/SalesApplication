namespace SalesApp.AI.Claude;

public static class AiCost
{
    /// <summary>
    /// Estimated cost in US dollars from the configured price list. A model missing from the list costs 0, so a
    /// newly configured model never breaks a call; it just isn't priced until the list is updated.
    /// </summary>
    public static decimal Estimate(ModelPricing? pricing, int input, int output, int cacheRead, int cacheWrite)
    {
        if (pricing is null)
        {
            return 0m;
        }

        var cost = (input * pricing.Input + output * pricing.Output + cacheRead * pricing.CacheRead + cacheWrite * pricing.CacheWrite) / 1_000_000m;
        return Math.Round(cost, 6);
    }
}
