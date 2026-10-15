using Microsoft.Extensions.Time.Testing;
using SalesApp.Core.Common;

namespace SalesApp.Tests.Common;

public sealed class BusinessClockTests
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById(BusinessClock.IndiaTimeZoneId);

    [Fact]
    public void Today_follows_india_time_not_utc()
    {
        // 20:00 UTC on 31 March is 01:30 IST on 1 April: already the new financial year in India.
        var clock = new BusinessClock(new FakeTimeProvider(new DateTimeOffset(2027, 3, 31, 20, 0, 0, TimeSpan.Zero)), India);

        Assert.Equal(new DateOnly(2027, 4, 1), clock.Today);
        Assert.Equal("27-28", FinancialYear.Label(clock.Today));
    }

    [Theory]
    [InlineData(2026, 4, 1, "26-27", 2026)]
    [InlineData(2027, 3, 31, "26-27", 2026)]
    [InlineData(2026, 10, 13, "26-27", 2026)]
    [InlineData(2026, 1, 15, "25-26", 2025)]
    [InlineData(2099, 6, 1, "99-00", 2099)]
    public void Financial_year_runs_april_to_march(int year, int month, int day, string label, int startYear)
    {
        var date = new DateOnly(year, month, day);

        Assert.Equal(label, FinancialYear.Label(date));
        Assert.Equal(startYear, FinancialYear.StartYear(date));
        Assert.Equal(new DateOnly(startYear, 4, 1), FinancialYear.Start(date));
        Assert.Equal(new DateOnly(startYear + 1, 3, 31), FinancialYear.End(date));
    }
}
