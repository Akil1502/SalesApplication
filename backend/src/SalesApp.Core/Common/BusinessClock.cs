namespace SalesApp.Core.Common;

/// <summary>
/// "Today" for the business, which is in India regardless of the server's time zone. A server in UTC
/// would otherwise date anything entered before 05:30 IST on the previous day. Also knows the Indian
/// financial year (April to March), which drives invoice numbering and reports.
/// </summary>
public interface IBusinessClock
{
    DateOnly Today { get; }

    TimeZoneInfo TimeZone { get; }

    /// <summary>The UTC instant converted to the business time zone's calendar date.</summary>
    DateOnly ToBusinessDate(DateTime utc);
}

public sealed class BusinessClock(TimeProvider timeProvider, TimeZoneInfo timeZone) : IBusinessClock
{
    /// <summary>IANA id; .NET resolves it on Windows too (via ICU), so the same config works everywhere.</summary>
    public const string IndiaTimeZoneId = "Asia/Kolkata";

    public TimeZoneInfo TimeZone => timeZone;

    public DateOnly Today => ToBusinessDate(timeProvider.GetUtcNow().UtcDateTime);

    public DateOnly ToBusinessDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone));
}

/// <summary>Indian financial year helpers: FY 2026-27 runs from 1 April 2026 to 31 March 2027.</summary>
public static class FinancialYear
{
    /// <summary>The calendar year the financial year containing <paramref name="date"/> starts in.</summary>
    public static int StartYear(DateOnly date) => date.Month >= 4 ? date.Year : date.Year - 1;

    /// <summary>Short label used in document numbers, e.g. "26-27".</summary>
    public static string Label(DateOnly date)
    {
        var start = StartYear(date);
        return $"{start % 100:D2}-{(start + 1) % 100:D2}";
    }

    public static DateOnly Start(DateOnly date) => new(StartYear(date), 4, 1);

    public static DateOnly End(DateOnly date) => new(StartYear(date) + 1, 3, 31);
}
