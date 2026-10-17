using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Reports;

namespace SalesApp.Tests.Common;

public sealed class DashboardPeriodsTests
{
    private static PeriodWindow Window(DashboardPeriod period, string today) => DashboardPeriods.Window(period, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));

    private static PeriodWindow Expected(string from, string to, string comparisonFrom, string comparisonTo) => new(
        DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
        DateOnly.Parse(comparisonFrom, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(comparisonTo, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("2026-10-17", "2026-10-01", "2026-10-17", "2026-09-01", "2026-09-17")]
    [InlineData("2026-03-31", "2026-03-01", "2026-03-31", "2026-02-01", "2026-02-28")]
    [InlineData("2026-01-05", "2026-01-01", "2026-01-05", "2025-12-01", "2025-12-05")]
    public void This_month_is_compared_with_the_same_days_of_last_month(string today, string from, string to, string comparisonFrom, string comparisonTo) =>
        Assert.Equal(Expected(from, to, comparisonFrom, comparisonTo), Window(DashboardPeriod.Month, today));

    [Theory]
    [InlineData("2026-10-17", "2026-10-01", "2026-10-17", "2026-07-01", "2026-07-17")]
    [InlineData("2026-05-20", "2026-04-01", "2026-05-20", "2026-01-01", "2026-02-19")]
    [InlineData("2026-03-31", "2026-01-01", "2026-03-31", "2025-10-01", "2025-12-29")]
    [InlineData("2026-06-30", "2026-04-01", "2026-06-30", "2026-01-01", "2026-03-31")]
    public void Quarters_follow_the_financial_year_and_compare_the_same_number_of_days(string today, string from, string to, string comparisonFrom, string comparisonTo) =>
        Assert.Equal(Expected(from, to, comparisonFrom, comparisonTo), Window(DashboardPeriod.Quarter, today));

    [Theory]
    [InlineData("2026-10-17", "2026-04-01", "2026-10-17", "2025-04-01", "2025-10-17")]
    [InlineData("2027-02-10", "2026-04-01", "2027-02-10", "2025-04-01", "2026-02-10")]
    public void This_financial_year_is_compared_with_the_same_days_a_year_earlier(string today, string from, string to, string comparisonFrom, string comparisonTo) =>
        Assert.Equal(Expected(from, to, comparisonFrom, comparisonTo), Window(DashboardPeriod.Year, today));

    [Fact]
    public void Last_financial_year_is_the_whole_year_compared_with_the_one_before() =>
        Assert.Equal(Expected("2025-04-01", "2026-03-31", "2024-04-01", "2025-03-31"), Window(DashboardPeriod.LastYear, "2026-10-17"));
}
