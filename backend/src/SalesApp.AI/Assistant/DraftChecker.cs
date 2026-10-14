using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SalesApp.AI.Assistant;

/// <summary>
/// Finds the facts in a drafted message that can be checked: document numbers (INV/26-27/00042) and exact rupee
/// amounts. Amounts are compared with every number in the tool results Claude saw in the conversation; rounded
/// figures ("₹1.2 lakh") are left alone.
/// </summary>
public static partial class DraftChecker
{
    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{0,9}/\d{2}-\d{2}/\d{3,6}\b")]
    private static partial Regex DocumentNumberPattern();

    [GeneratedRegex(@"(?:₹|\bRs\.?|\bINR)\s?(\d[\d,]*(?:\.\d{1,2})?)(\s*(?:lakhs?|lacs?|crores?|cr)\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex AmountPattern();

    public static IReadOnlyList<string> DocumentNumbers(string text) =>
        [.. DocumentNumberPattern().Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal)];

    /// <summary>Exact amounts written in the text (with ₹, Rs or INR), skipping ones given in lakh or crore.</summary>
    public static IReadOnlyList<(string Text, decimal Value)> ExactAmounts(string text)
    {
        var amounts = new List<(string, decimal)>();
        foreach (Match match in AmountPattern().Matches(text))
        {
            if (match.Groups[2].Success)
            {
                continue;
            }

            var digits = match.Groups[1].Value.Replace(",", "", StringComparison.Ordinal).TrimEnd('.');
            if (decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                amounts.Add((match.Value.Trim().TrimEnd(','), value));
            }
        }

        return amounts;
    }

    /// <summary>Every number anywhere in the given JSON tool results (non-JSON results are skipped).</summary>
    public static HashSet<decimal> NumbersIn(IEnumerable<string> toolResults)
    {
        var numbers = new HashSet<decimal>();
        foreach (var result in toolResults)
        {
            try
            {
                using var document = JsonDocument.Parse(result);
                Collect(document.RootElement, numbers);
            }
            catch (JsonException)
            {
                // Error messages and other plain text hold no figures worth matching.
            }
        }

        return numbers;
    }

    /// <summary>The amounts in <paramref name="body"/> that don't match any number in <paramref name="known"/> (to the paisa).</summary>
    public static IReadOnlyList<string> UnknownAmounts(string body, HashSet<decimal> known) =>
        [.. ExactAmounts(body).Where(a => !known.Contains(decimal.Round(a.Value, 2))).Select(a => a.Text).Distinct(StringComparer.Ordinal)];

    private static void Collect(JsonElement element, HashSet<decimal> numbers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDecimal(out var number):
                numbers.Add(decimal.Round(number, 2));
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
