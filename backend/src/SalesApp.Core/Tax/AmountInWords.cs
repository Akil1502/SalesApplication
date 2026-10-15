using System.Globalization;
using System.Text;

namespace SalesApp.Core.Tax;

/// <summary>
/// Rupee amounts in words using the Indian numbering system (thousand, lakh, crore), as printed on
/// invoices: 123456.50 -> "Rupees One Lakh Twenty Three Thousand Four Hundred Fifty Six and Fifty Paise Only".
/// </summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Rupees(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount can't be negative.");
        }

        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = (long)Math.Floor(rounded);
        var paise = (int)((rounded - rupees) * 100);

        var text = new StringBuilder("Rupees ").Append(Words(rupees));
        if (paise > 0)
        {
            text.Append(" and ").Append(Words(paise)).Append(" Paise");
        }

        return text.Append(" Only").ToString();
    }

    /// <summary>A whole number in words, Indian style: crore (10^7), lakh (10^5), thousand, hundred.</summary>
    public static string Words(long number)
    {
        if (number == 0)
        {
            return Ones[0];
        }

        var parts = new List<string>();
        // Above 99 crore, the crore count itself is spelled with lakh/thousand (e.g. "One Hundred Crore").
        if (number >= 10_000_000)
        {
            parts.Add($"{Words(number / 10_000_000)} Crore");
            number %= 10_000_000;
        }

        AddGroup(parts, ref number, 100_000, "Lakh");
        AddGroup(parts, ref number, 1_000, "Thousand");
        AddGroup(parts, ref number, 100, "Hundred");

        if (number > 0)
        {
            parts.Add(UnderHundred((int)number));
        }

        return string.Join(' ', parts);
    }

    private static void AddGroup(List<string> parts, ref long number, long unit, string name)
    {
        if (number >= unit)
        {
            parts.Add($"{UnderHundred((int)(number / unit))} {name}");
            number %= unit;
        }
    }

    private static string UnderHundred(int n) =>
        n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : string.Empty);

    /// <summary>Formats with Indian digit grouping: 1234567.5 -> "12,34,567.50".</summary>
    public static string IndianGrouping(decimal amount) =>
        amount.ToString("#,##,##0.00", CultureInfo.GetCultureInfo("en-IN"));
}
