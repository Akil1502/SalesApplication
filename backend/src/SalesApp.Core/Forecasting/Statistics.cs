namespace SalesApp.Core.Forecasting;

/// <summary>Small statistics helpers for anomaly detection and seasonal adjustment.</summary>
public static class Statistics
{
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// How unusual <paramref name="value"/> is among <paramref name="history"/>, in standard deviations, using the
    /// median and the median absolute deviation so the outliers being looked for don't hide themselves by
    /// inflating the spread. Zero when the history is too short or has no spread at all.
    /// </summary>
    public static double RobustZ(double value, IReadOnlyCollection<double> history)
    {
        if (history.Count < 4)
        {
            return 0;
        }

        var median = Median(history);
        var mad = Median(history.Select(v => Math.Abs(v - median))) * 1.4826;
        if (mad < 1e-9)
        {
            // Mostly identical values: fall back to the mean absolute deviation.
            mad = history.Average(v => Math.Abs(v - median)) * 1.2533;
        }

        return mad < 1e-9 ? 0 : (value - median) / mad;
    }

    /// <summary>
    /// Seasonal index per calendar month (average 1.0): each month's sales divided by the average of the twelve
    /// months ending with it, averaged across years. Months without enough history get 1.0.
    /// </summary>
    public static double[] MonthlySeasonalIndex(IReadOnlyList<(DateOnly Month, double Value)> series)
    {
        var ratios = new List<double>[12];
        for (var i = 0; i < 12; i++)
        {
            ratios[i] = [];
        }

        for (var t = 11; t < series.Count; t++)
        {
            var trailing = 0.0;
            for (var j = t - 11; j <= t; j++)
            {
                trailing += series[j].Value;
            }

            trailing /= 12;
            if (trailing > 0)
            {
                ratios[series[t].Month.Month - 1].Add(series[t].Value / trailing);
            }
        }

        var index = ratios.Select(r => r.Count > 0 ? r.Average() : 1.0).ToArray();
        var mean = index.Average();
        return mean > 0 ? [.. index.Select(v => v / mean)] : [.. Enumerable.Repeat(1.0, 12)];
    }
}

/// <summary>When to reorder a product and how much, from its recent demand and supplier lead time.</summary>
public sealed record ReorderPlan(
    double DailyDemand,
    double SafetyStock,
    double ReorderPoint,
    double DaysOfCover,
    bool ShouldReorder,
    bool RunsOutBeforeDelivery,
    double SuggestedQuantity);

public static class Reorder
{
    /// <summary>About a 95% chance of not running out while waiting for a delivery.</summary>
    public const double ServiceLevelZ = 1.65;

    /// <summary>How long an order should last beyond the lead time.</summary>
    public const int ReviewDays = 30;

    /// <summary>
    /// Reorder point = expected demand over the lead time + safety stock (z × daily demand spread × √lead time).
    /// When stock is at or below it, suggest enough to cover the lead time and the review period, and at least
    /// the product's usual reorder quantity.
    /// </summary>
    public static ReorderPlan Plan(double dailyDemand, double dailyStdDev, int leadTimeDays, double onHand, double reorderQuantity)
    {
        var lead = Math.Max(leadTimeDays, 1);
        var safety = ServiceLevelZ * dailyStdDev * Math.Sqrt(lead);
        var reorderPoint = dailyDemand * lead + safety;
        var cover = dailyDemand > 0 ? Math.Max(onHand, 0) / dailyDemand : double.PositiveInfinity;
        var shouldReorder = dailyDemand > 0 && onHand <= reorderPoint;
        var suggested = shouldReorder
            ? Math.Ceiling(Math.Max(reorderQuantity, dailyDemand * (lead + ReviewDays) + safety - Math.Max(onHand, 0)))
            : 0;
        return new ReorderPlan(dailyDemand, safety, reorderPoint, cover, shouldReorder, shouldReorder && cover < lead, suggested);
    }
}
