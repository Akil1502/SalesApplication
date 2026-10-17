using SalesApp.Core.Contracts.Reports;

namespace SalesApp.Core.Common;

/// <summary>The days a dashboard period covers, and the earlier days it's compared with.</summary>
public sealed record PeriodWindow(DateOnly From, DateOnly To, DateOnly ComparisonFrom, DateOnly ComparisonTo);

public static class DashboardPeriods
{
    /// <summary>
    /// Periods run to today. Each is compared like for like: this month with the same days of last month, this quarter
    /// with the same days of last quarter, this financial year with the same days a year earlier, and last financial
    /// year with the one before. Quarters follow the financial year (April to June first), which matches the calendar's.
    /// </summary>
    public static PeriodWindow Window(DashboardPeriod period, DateOnly today)
    {
        switch (period)
        {
            case DashboardPeriod.Month:
            {
                var from = new DateOnly(today.Year, today.Month, 1);
                var earlier = from.AddMonths(-1);
                return new PeriodWindow(from, today, earlier, SameDay(earlier, from.AddDays(-1), today.DayNumber - from.DayNumber));
            }

            case DashboardPeriod.Quarter:
            {
                var from = new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1);
                var earlier = from.AddMonths(-3);
                return new PeriodWindow(from, today, earlier, SameDay(earlier, from.AddDays(-1), today.DayNumber - from.DayNumber));
            }

            case DashboardPeriod.LastYear:
            {
                var from = FinancialYear.Start(today).AddYears(-1);
                return new PeriodWindow(from, from.AddYears(1).AddDays(-1), from.AddYears(-1), from.AddDays(-1));
            }

            default:
            {
                var from = FinancialYear.Start(today);
                return new PeriodWindow(from, today, from.AddYears(-1), today.AddYears(-1));
            }
        }
    }

    /// <summary>The same number of days into an earlier span, stopping at its end (31 March has no 31 February).</summary>
    private static DateOnly SameDay(DateOnly start, DateOnly end, int daysIn)
    {
        var day = start.AddDays(daysIn);
        return day > end ? end : day;
    }
}
