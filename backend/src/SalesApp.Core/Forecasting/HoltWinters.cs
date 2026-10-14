namespace SalesApp.Core.Forecasting;

public enum SeasonalityKind
{
    /// <summary>The seasonal swing is a fixed amount (e.g. +₹2 lakh every October).</summary>
    Additive,

    /// <summary>The seasonal swing is a share of the level (e.g. +45% every October); suits a growing business.</summary>
    Multiplicative,
}

/// <summary>
/// A fitted Holt-Winters model: smoothing parameters, the damping of the trend (<see cref="Phi"/>, 1 = none), and
/// the final level, trend and seasonal factors.
/// </summary>
public sealed record HoltWintersModel(
    SeasonalityKind Kind,
    int SeasonLength,
    double Alpha,
    double Beta,
    double Gamma,
    double Phi,
    double Level,
    double Trend,
    IReadOnlyList<double> LastSeason,
    double SumSquaredError)
{
    /// <summary>Forecasts for the next <paramref name="horizon"/> periods after the fitted series.</summary>
    public double[] Forecast(int horizon)
    {
        var forecast = new double[horizon];
        var damping = 0.0;
        for (var k = 1; k <= horizon; k++)
        {
            damping += Math.Pow(Phi, k);
            var seasonal = LastSeason[(k - 1) % SeasonLength];
            var baseline = Level + damping * Trend;
            forecast[k - 1] = Kind == SeasonalityKind.Multiplicative ? baseline * seasonal : baseline + seasonal;
        }

        return forecast;
    }
}

/// <summary>
/// Triple exponential smoothing (level, trend and seasonality) with a damped trend, fitted by grid search on
/// one-step-ahead squared error. Needs two full seasons: the starting trend comes from comparing them, and the
/// first season is measured against that trend line, so a growing business's first-year growth doesn't leak into
/// the seasonal factors. The trend is learnt slowly and may be damped; an eager trend overshoots on short histories.
/// </summary>
public static class HoltWinters
{
    private static readonly double[] Grid = [0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];
    private static readonly double[] TrendGrid = [0.01, 0.05, 0.1, 0.2];
    private static readonly double[] DampingGrid = [0.8, 0.9, 0.98, 1.0];

    public static int MinimumPoints(int seasonLength) => 2 * seasonLength;

    public static HoltWintersModel Fit(IReadOnlyList<double> series, int seasonLength, SeasonalityKind kind)
    {
        if (series.Count < MinimumPoints(seasonLength))
        {
            throw new ArgumentException($"Holt-Winters needs at least {MinimumPoints(seasonLength)} points.", nameof(series));
        }

        if (kind == SeasonalityKind.Multiplicative && series.Any(v => v <= 0))
        {
            throw new ArgumentException("Multiplicative seasonality needs every value to be positive.", nameof(series));
        }

        HoltWintersModel? best = null;
        foreach (var alpha in Grid)
        {
            foreach (var beta in TrendGrid)
            {
                foreach (var gamma in Grid)
                {
                    foreach (var phi in DampingGrid)
                    {
                        var model = Run(series, seasonLength, kind, alpha, beta, gamma, phi);
                        if (best is null || model.SumSquaredError < best.SumSquaredError)
                        {
                            best = model;
                        }
                    }
                }
            }
        }

        return best!;
    }

    internal static HoltWintersModel Run(IReadOnlyList<double> y, int m, SeasonalityKind kind, double alpha, double beta, double gamma, double phi)
    {
        var multiplicative = kind == SeasonalityKind.Multiplicative;
        var firstSeason = y.Take(m).Average();
        var trend = (y.Skip(m).Take(m).Average() - firstSeason) / m;
        // The first season's average belongs to its middle; seasonals are measured against the trend line through it.
        var seasonals = new List<double>(y.Count);
        for (var i = 0; i < m; i++)
        {
            var line = firstSeason + (i - (m - 1) / 2.0) * trend;
            seasonals.Add(multiplicative ? y[i] / line : y[i] - line);
        }

        var level = firstSeason + (m - 1) / 2.0 * trend;

        var sse = 0.0;
        for (var t = m; t < y.Count; t++)
        {
            var seasonal = seasonals[t - m];
            var forecast = multiplicative ? (level + phi * trend) * seasonal : level + phi * trend + seasonal;
            var error = y[t] - forecast;
            sse += error * error;

            var previousLevel = level;
            level = multiplicative
                ? alpha * (y[t] / seasonal) + (1 - alpha) * (level + phi * trend)
                : alpha * (y[t] - seasonal) + (1 - alpha) * (level + phi * trend);
            trend = beta * (level - previousLevel) + (1 - beta) * phi * trend;
            seasonals.Add(multiplicative ? gamma * (y[t] / level) + (1 - gamma) * seasonal : gamma * (y[t] - level) + (1 - gamma) * seasonal);
        }

        return new HoltWintersModel(kind, m, alpha, beta, gamma, phi, level, trend, seasonals.Skip(seasonals.Count - m).ToArray(), sse);
    }
}
