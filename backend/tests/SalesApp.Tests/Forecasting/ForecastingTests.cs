using SalesApp.Core.Forecasting;

namespace SalesApp.Tests.Forecasting;

public sealed class ForecasterTests
{
    /// <summary>The demo company's pattern: festive Oct-Nov peak, April-May lull (index by calendar month, Jan first).</summary>
    private static readonly double[] Season = [0.95, 0.90, 1.05, 0.85, 0.85, 0.90, 0.95, 1.05, 1.15, 1.45, 1.35, 1.00];

    /// <summary>Monthly sales starting in April, growing 18% a year, with optional multiplicative noise.</summary>
    private static List<double> Series(int months, double noise = 0, int seed = 7)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, months).Select(t =>
            1_000_000 * Season[(t + 3) % 12] * Math.Pow(1.18, t / 12.0) * (1 + noise * (random.NextDouble() * 2 - 1)))];
    }

    [Fact]
    public void Seasonal_naive_with_growth_is_exact_on_a_clean_seasonal_series()
    {
        // Growth compounded per year, so same-month-last-year times annual growth is exactly right.
        var series = Enumerable.Range(0, 36).Select(t => 100.0 * Season[t % 12] * Math.Pow(1.18, t / 12)).ToList();

        var forecast = Forecaster.SeasonalNaive(series, 3);

        for (var k = 0; k < 3; k++)
        {
            Assert.Equal(100.0 * Season[k] * Math.Pow(1.18, 3), forecast[k], 6);
        }
    }

    [Fact]
    public void Holt_winters_learns_trend_and_seasonality()
    {
        var series = Series(36, noise: 0.03);
        var truth = Series(39).Skip(36).ToList();

        var model = HoltWinters.Fit(series, 12, SeasonalityKind.Multiplicative);
        var forecast = model.Forecast(3);

        Assert.True(Forecaster.Mape(truth, forecast) < 5, $"MAPE {Forecaster.Mape(truth, forecast):F1}%");
        Assert.Throws<ArgumentException>(() => HoltWinters.Fit(series.Take(23).ToList(), 12, SeasonalityKind.Additive));
    }

    [Fact]
    public void Picks_the_method_with_the_lowest_backtest_error_and_shows_every_backtest()
    {
        var series = Series(36, noise: 0.04);

        var result = Forecaster.Forecast(series, 3);

        Assert.Equal(6, result.HoldoutMonths);
        Assert.Equal(
            [ForecastMethod.SeasonalNaive, ForecastMethod.HoltWintersAdditive, ForecastMethod.HoltWintersMultiplicative],
            result.Backtests.Select(b => b.Method));
        var best = result.Backtests.MinBy(b => b.MeanAbsolutePercentError)!;
        Assert.Equal(best.Method, result.Method);
        Assert.True(best.MeanAbsolutePercentError < 10, $"best backtest MAPE {best.MeanAbsolutePercentError:F1}%");
        Assert.Equal(3, result.Points.Count);
    }

    [Fact]
    public void The_forecast_is_close_on_realistic_data()
    {
        var all = Series(29, noise: 0.05, seed: 11);
        var history = all.Take(26).ToList();
        var truth = Series(29, seed: 11).Skip(26).ToList(); // the noise-free pattern the noisy history came from

        var result = Forecaster.Forecast(history, 3);

        Assert.True(Forecaster.Mape(truth, [.. result.Points.Select(p => p.Value)]) < 10);
    }

    [Fact]
    public void The_range_contains_the_forecast_and_widens_further_out()
    {
        var result = Forecaster.Forecast(Series(26, noise: 0.04), 3);

        Assert.All(result.Points, p => Assert.True(p.Low < p.Value && p.Value < p.High));
        var widths = result.Points.Select(p => (p.High - p.Low) / p.Value).ToList();
        Assert.True(widths[0] < widths[1] && widths[1] < widths[2]);
    }

    [Fact]
    public void Short_histories_fall_back_to_simpler_methods()
    {
        Assert.Equal(ForecastMethod.None, Forecaster.Forecast([5, 6], 3).Method);

        var eightMonths = Forecaster.Forecast(Series(8), 3);
        Assert.Equal(ForecastMethod.RecentAverage, eightMonths.Method);
        Assert.Empty(eightMonths.Backtests);
        Assert.Equal(Series(8).TakeLast(3).Average(), eightMonths.Points[0].Value, 6);

        var thirteenMonths = Forecaster.Forecast(Series(13), 3);
        Assert.Equal(ForecastMethod.SeasonalNaive, thirteenMonths.Method);

        // Under two years of training data, Holt-Winters isn't trusted: only the baseline is backtested.
        var twoYears = Forecaster.Forecast(Series(24, noise: 0.04), 3);
        Assert.Equal([ForecastMethod.SeasonalNaive], twoYears.Backtests.Select(b => b.Method));
        Assert.Equal(ForecastMethod.SeasonalNaive, twoYears.Method);
    }

    [Fact]
    public void Seasonal_naive_estimates_growth_from_the_months_it_has()
    {
        // Sixteen months: four months overlap with the year before, enough to measure 18% growth.
        var series = Enumerable.Range(0, 16).Select(t => 100.0 * Math.Pow(1.18, t / 12)).ToList();

        Assert.Equal(100 * 1.18, Forecaster.SeasonalNaive(series, 1)[0], 6); // same month last year, plus growth
    }

    [Fact]
    public void A_month_with_no_sales_rules_out_multiplicative_seasonality()
    {
        var series = Series(36, noise: 0.04);
        series[5] = 0;

        var result = Forecaster.Forecast(series, 3);

        Assert.DoesNotContain(result.Backtests, b => b.Method == ForecastMethod.HoltWintersMultiplicative);
        Assert.Throws<ArgumentException>(() => HoltWinters.Fit(series, 12, SeasonalityKind.Multiplicative));
    }

    [Fact]
    public void Mape_ignores_months_with_no_actual_sales()
    {
        Assert.Equal(10, Forecaster.Mape([100, 0, 200], [110, 50, 180]), 6);
    }
}

public sealed class StatisticsTests
{
    [Fact]
    public void A_spike_stands_out_and_ordinary_variation_does_not()
    {
        double[] history = [100, 104, 97, 101, 99, 103, 98, 102, 100, 96, 105];

        Assert.True(Statistics.RobustZ(300, history) > 10);
        Assert.InRange(Statistics.RobustZ(103, history), -2, 2);
        Assert.True(Statistics.RobustZ(10, history) < -10);
    }

    [Fact]
    public void Robust_z_is_zero_without_enough_history_or_spread()
    {
        Assert.Equal(0, Statistics.RobustZ(500, [1, 2, 3]));
        Assert.Equal(0, Statistics.RobustZ(500, [5, 5, 5, 5, 5]));
    }

    [Fact]
    public void The_seasonal_index_recovers_the_monthly_pattern()
    {
        double[] season = [0.95, 0.90, 1.05, 0.85, 0.85, 0.90, 0.95, 1.05, 1.15, 1.45, 1.35, 1.00];
        var start = new DateOnly(2024, 4, 1);
        var series = Enumerable.Range(0, 30)
            .Select(t => (start.AddMonths(t), 1000 * season[start.AddMonths(t).Month - 1] * Math.Pow(1.18, t / 12.0)))
            .ToList();

        var index = Statistics.MonthlySeasonalIndex(series);

        var expected = season.Select(s => s / season.Average()).ToArray();
        for (var m = 0; m < 12; m++)
        {
            Assert.InRange(index[m], expected[m] * 0.93, expected[m] * 1.07);
        }
    }

    [Fact]
    public void Median_handles_odd_even_and_empty()
    {
        Assert.Equal(3, Statistics.Median([5, 1, 3]));
        Assert.Equal(2.5, Statistics.Median([4, 1, 3, 2]));
        Assert.Equal(0, Statistics.Median([]));
    }
}

public sealed class ReorderTests
{
    [Fact]
    public void Reorders_when_stock_falls_to_the_reorder_point()
    {
        // 20 kg a day, spread 6 kg, 9-day lead time: safety = 1.65 × 6 × 3 = 29.7; reorder point = 180 + 29.7.
        var plan = Reorder.Plan(dailyDemand: 20, dailyStdDev: 6, leadTimeDays: 9, onHand: 150, reorderQuantity: 500);

        Assert.Equal(29.7, plan.SafetyStock, 6);
        Assert.Equal(209.7, plan.ReorderPoint, 6);
        Assert.True(plan.ShouldReorder);
        Assert.Equal(7.5, plan.DaysOfCover, 6);
        Assert.True(plan.RunsOutBeforeDelivery); // 7.5 days of stock, 9 days to deliver
        Assert.Equal(Math.Ceiling(20 * 39 + 29.7 - 150), plan.SuggestedQuantity); // covers lead time + 30 days
    }

    [Fact]
    public void Suggests_at_least_the_usual_reorder_quantity()
    {
        var plan = Reorder.Plan(dailyDemand: 2, dailyStdDev: 1, leadTimeDays: 10, onHand: 20, reorderQuantity: 500);

        Assert.True(plan.ShouldReorder);
        Assert.False(plan.RunsOutBeforeDelivery);
        Assert.Equal(500, plan.SuggestedQuantity);
    }

    [Fact]
    public void No_suggestion_with_enough_stock_or_no_demand()
    {
        Assert.False(Reorder.Plan(20, 6, 9, 1000, 500).ShouldReorder);
        var idle = Reorder.Plan(0, 0, 9, 0, 500);
        Assert.False(idle.ShouldReorder);
        Assert.Equal(double.PositiveInfinity, idle.DaysOfCover);
    }
}
