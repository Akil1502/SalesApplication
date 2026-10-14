namespace SalesApp.Core.Forecasting;

public enum ForecastMethod
{
    /// <summary>Not enough history to forecast.</summary>
    None,

    /// <summary>The average of the last three months, for short histories.</summary>
    RecentAverage,

    /// <summary>The same month last year, scaled by the year-on-year growth: the baseline to beat.</summary>
    SeasonalNaive,

    HoltWintersAdditive,
    HoltWintersMultiplicative,
}

/// <summary>How a method did when it forecast the most recent months from the data before them.</summary>
public sealed record Backtest(ForecastMethod Method, double MeanAbsolutePercentError);

public sealed record ForecastPoint(int Step, double Value, double Low, double High);

/// <summary>
/// The forecast, the method that produced it, and every method's backtest, so the choice can be seen and
/// questioned. Each point's <see cref="ForecastPoint.Low"/> and <see cref="ForecastPoint.High"/> give a rough 80% range
/// from the backtest errors.
/// </summary>
public sealed record ForecastResult(ForecastMethod Method, IReadOnlyList<Backtest> Backtests, IReadOnlyList<ForecastPoint> Points, int HoldoutMonths);

/// <summary>
/// Picks a forecasting method by backtest: each candidate forecasts the last few months from the months before
/// them, and the one with the lowest mean absolute percentage error (MAPE) is refitted on all the data. A
/// candidate has to beat the seasonal-naive baseline to be chosen; ties go to the simpler method. Holt-Winters
/// joins the comparison once there are two full years of training data before the backtest months.
/// </summary>
public static class Forecaster
{
    public const int SeasonLength = 12;
    private const int MaxHoldout = 6;
    private const double DefaultRelativeError = 0.15;
    private const double Z80 = 1.2816;

    public static ForecastResult Forecast(IReadOnlyList<double> monthly, int horizon)
    {
        if (monthly.Count < 3)
        {
            return new ForecastResult(ForecastMethod.None, [], [], 0);
        }

        // Keep at least a year and a month of training data, so the seasonal baseline can be backtested.
        var holdout = Math.Min(MaxHoldout, monthly.Count - (SeasonLength + 1));
        var backtests = new List<Backtest>();
        if (holdout >= 1)
        {
            var training = monthly.Take(monthly.Count - holdout).ToList();
            var actual = monthly.Skip(monthly.Count - holdout).ToList();
            foreach (var method in Candidates(training))
            {
                backtests.Add(new Backtest(method, Mape(actual, Predict(method, training, holdout))));
            }
        }

        var chosen = backtests.Count == 0
            ? monthly.Count > SeasonLength ? ForecastMethod.SeasonalNaive : ForecastMethod.RecentAverage
            : backtests.OrderBy(b => Math.Round(b.MeanAbsolutePercentError, 6)).ThenBy(b => Complexity(b.Method)).First().Method;

        var relativeError = backtests.FirstOrDefault(b => b.Method == chosen)?.MeanAbsolutePercentError / 100 ?? DefaultRelativeError;
        // MAPE is a mean absolute error; for a normal error that's about 0.8 of the standard deviation.
        var sigma = Math.Max(relativeError, 0.01) / 0.7979;
        var values = Predict(chosen, monthly, horizon);
        var points = values
            .Select((value, i) =>
            {
                var spread = Z80 * sigma * Math.Sqrt(i + 1) * Math.Abs(value);
                return new ForecastPoint(i + 1, value, Math.Max(0, value - spread), value + spread);
            })
            .ToList();
        return new ForecastResult(chosen, backtests, points, Math.Max(holdout, 0));
    }

    private static IEnumerable<ForecastMethod> Candidates(List<double> training)
    {
        if (training.Count > SeasonLength)
        {
            yield return ForecastMethod.SeasonalNaive;
        }

        if (training.Count >= HoltWinters.MinimumPoints(SeasonLength))
        {
            yield return ForecastMethod.HoltWintersAdditive;
            if (training.All(v => v > 0))
            {
                yield return ForecastMethod.HoltWintersMultiplicative;
            }
        }

        if (training.Count <= SeasonLength)
        {
            yield return ForecastMethod.RecentAverage;
        }
    }

    internal static double[] Predict(ForecastMethod method, IReadOnlyList<double> series, int horizon) => method switch
    {
        ForecastMethod.SeasonalNaive => SeasonalNaive(series, horizon),
        ForecastMethod.HoltWintersAdditive => HoltWinters.Fit(series, SeasonLength, SeasonalityKind.Additive).Forecast(horizon),
        ForecastMethod.HoltWintersMultiplicative => HoltWinters.Fit(series, SeasonLength, SeasonalityKind.Multiplicative).Forecast(horizon),
        ForecastMethod.RecentAverage => Enumerable.Repeat(series.TakeLast(3).Average(), horizon).ToArray(),
        _ => [],
    };

    /// <summary>
    /// The same month a year earlier, times the year-on-year growth: the latest months (up to twelve) against the
    /// same months a year before. Needs three overlapping months to estimate growth; with fewer, growth is 1.
    /// </summary>
    public static double[] SeasonalNaive(IReadOnlyList<double> series, int horizon)
    {
        var n = series.Count;
        var growth = 1.0;
        var overlap = Math.Min(SeasonLength, n - SeasonLength);
        if (overlap >= 3)
        {
            var yearBefore = series.Skip(n - overlap - SeasonLength).Take(overlap).Sum();
            growth = yearBefore > 0 ? series.Skip(n - overlap).Sum() / yearBefore : 1.0;
        }

        var forecast = new double[horizon];
        for (var k = 1; k <= horizon; k++)
        {
            // Beyond a year ahead, repeat the last year's pattern with growth compounded.
            var yearsBack = (k - 1) / SeasonLength + 1;
            forecast[k - 1] = series[n - SeasonLength + (k - 1) % SeasonLength] * Math.Pow(growth, yearsBack);
        }

        return forecast;
    }

    public static double Mape(IReadOnlyList<double> actual, IReadOnlyList<double> forecast)
    {
        var errors = actual.Zip(forecast).Where(p => p.First != 0).Select(p => Math.Abs((p.First - p.Second) / p.First)).ToList();
        return errors.Count == 0 ? 0 : errors.Average() * 100;
    }

    private static int Complexity(ForecastMethod method) => method switch
    {
        ForecastMethod.RecentAverage => 0,
        ForecastMethod.SeasonalNaive => 1,
        ForecastMethod.HoltWintersAdditive => 2,
        _ => 3,
    };
}
