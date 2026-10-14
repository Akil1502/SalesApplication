using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SalesApp.AI.Insights;

/// <summary>
/// Checks that every figure in a piece of the written summary appears in the metrics it was written from. A figure
/// may be rounded the way people write it (₹1,87,17,602 as "₹1.87 crore", 18.66% as "19%"), compared at the
/// precision it's written in. Dates, financial-year labels, document numbers, product codes and small counts of
/// days or months ("last 6 months") aren't figures and are skipped.
/// </summary>
public static partial class NarrativeNumberCheck
{
    [GeneratedRegex(@"(?<cur>₹\s?|\bRs\.?\s?|\bINR\s?)?(?<num>\d{1,3}(?:,\d{2,3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?<suf>\s?(?:%|per\s?cent\b|lakhs?\b|lacs?\b|crores?\b|cr\b|L\b))?", RegexOptions.IgnoreCase)]
    private static partial Regex Figure();

    // Removed before looking for figures: dates ("14 Oct 2026", "Oct 2026", "2026-10-14"), financial years
    // ("2026-27", "FY 26-27"), document numbers ("INV/26-27/00042"), product and customer codes ("CY-60C", "C00025").
    [GeneratedRegex(
        @"\b[A-Z][A-Z0-9]{0,9}/\d{2}-\d{2}/\d+\b" +
        @"|\b\d{1,2}(?:st|nd|rd|th)?\s(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?,?\s\d{4}\b" +
        @"|\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s\d{1,2}(?:st|nd|rd|th)?,?\s\d{4}\b" +
        @"|\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s\d{4}\b" +
        @"|\b\d{1,2}(?:st|nd|rd|th)?\s(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\b" +
        @"|\b\d{4}-\d{2}(?:-\d{2})?\b|\b\d{2}-\d{2}\b" +
        @"|\b[A-Z]{1,5}-[A-Z0-9]+(?:-[A-Z0-9]+)*\b|\b[A-Z]\d{3,}\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotFigures();

    [GeneratedRegex(@"^\s*(?:days?|weeks?|months?|years?|quarters?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimeAfter();

    // "₹2.1–2.6 crore": the unit written once at the end applies to both ends of the range.
    [GeneratedRegex(@"\G\s?(?:–|-|to)\s?(?:₹\s?)?\d[\d,]*(?:\.\d+)?\s?(?<suf>lakhs?\b|lacs?\b|crores?\b|cr\b|L\b)", RegexOptions.IgnoreCase)]
    private static partial Regex RangeUnitAfter();

    [GeneratedRegex(@"\b(?:top|last|next|past|first|over|within|every)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CountWordBefore();

    /// <summary>Every number in the metrics JSON, including figures written in its text fields.</summary>
    public static List<decimal> NumbersIn(string metricsJson)
    {
        var numbers = new List<decimal>();
        using var document = JsonDocument.Parse(metricsJson);
        Collect(document.RootElement, numbers);
        return numbers;
    }

    /// <summary>The figures in <paramref name="text"/> that don't match any of <paramref name="known"/>.</summary>
    public static List<string> Unverified(string text, IReadOnlyCollection<decimal> known) =>
        [.. Figures(text).Where(f => !known.Any(k => Math.Abs(Math.Abs(k) - f.Amount) <= f.Tolerance)).Select(f => f.Text)];

    /// <summary>A figure as written, its value in plain units, and how far off it may be given how it was rounded.</summary>
    private readonly record struct FigureText(string Text, decimal Amount, decimal Tolerance);

    private static IEnumerable<FigureText> Figures(string text)
    {
        var cleaned = NotFigures().Replace(text, match => new string(' ', match.Length));
        foreach (Match match in Figure().Matches(cleaned))
        {
            var number = match.Groups["num"].Value;
            if (!decimal.TryParse(number.Replace(",", "", StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var currency = match.Groups["cur"].Success;
            var suffix = match.Groups["suf"].Value.Trim().ToLowerInvariant();
            if (suffix.Length == 0 && RangeUnitAfter().Match(cleaned, match.Index + match.Length) is { Success: true } range)
            {
                suffix = range.Groups["suf"].Value.Trim().ToLowerInvariant();
            }

            var decimals = number.Contains('.', StringComparison.Ordinal) ? number.Length - number.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
            if (!currency && suffix.Length == 0 && decimals == 0 && !number.Contains(',', StringComparison.Ordinal))
            {
                // A year on its own ("in 2026"), or a small count of days or months ("last 6 months").
                if (value is >= 1990 and <= 2100)
                {
                    continue;
                }

                var after = cleaned[(match.Index + match.Length)..];
                var before = cleaned[..match.Index];
                if (value <= 31 && (TimeAfter().IsMatch(after) || CountWordBefore().IsMatch(before)))
                {
                    continue;
                }
            }

            var scale = suffix switch
            {
                "lakh" or "lakhs" or "lac" or "lacs" or "l" => 100_000m,
                "crore" or "crores" or "cr" => 10_000_000m,
                _ => 1m,
            };
            yield return new FigureText(match.Value.Trim(), value * scale, 0.5m * Pow10(-decimals) * scale);
        }
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < Math.Abs(exponent); i++)
        {
            result = exponent < 0 ? result / 10 : result * 10;
        }

        return result;
    }

    private static void Collect(JsonElement element, List<decimal> numbers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDecimal(out var number):
                numbers.Add(number);
                break;
            case JsonValueKind.String:
                // The metrics' own sentences ("₹45,33,000 on 15 May 2026") are computed too, so their figures count.
                numbers.AddRange(Figures(element.GetString() ?? "").Select(f => f.Amount));
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, numbers);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, numbers);
                }

                break;
        }
    }
}
