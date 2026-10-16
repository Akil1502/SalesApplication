using SalesApp.Infrastructure.Services;

namespace SalesApp.Tests.Common;

public sealed class RateOverlapTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public void Periods_that_share_a_day_overlap()
    {
        Assert.True(RateService.Overlaps(D(1, 1), D(1, 31), D(1, 31), D(2, 28)));
        Assert.True(RateService.Overlaps(D(1, 1), D(1, 31), D(1, 10), D(1, 20)));
        Assert.True(RateService.Overlaps(D(1, 1), null, D(6, 1), D(6, 30)));
        Assert.True(RateService.Overlaps(D(3, 1), null, D(1, 1), null));
    }

    [Fact]
    public void Back_to_back_months_dont_overlap()
    {
        Assert.False(RateService.Overlaps(D(1, 1), D(1, 31), D(2, 1), D(2, 28)));
        Assert.False(RateService.Overlaps(D(2, 1), null, D(1, 1), D(1, 31)));
    }
}
