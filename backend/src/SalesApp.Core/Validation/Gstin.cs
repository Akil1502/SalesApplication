using System.Text.RegularExpressions;
using SalesApp.Core.Localization;

namespace SalesApp.Core.Validation;

/// <summary>
/// GSTIN (Goods and Services Tax Identification Number) checks. A GSTIN is 15 characters:
/// 2-digit state code, 10-character PAN, entity number, 'Z', and a check character computed
/// with the mod-36 algorithm GSTN publishes. Validating the checksum catches almost every typo.
/// </summary>
public static partial class Gstin
{
    private const string Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex FormatRegex();

    /// <summary>Upper-cases and strips spaces, so "33abcde 1234f1z7" is treated as "33ABCDE1234F1Z7".</summary>
    public static string Normalize(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    /// <summary>Returns null when valid, otherwise a message suitable for showing next to the field.</summary>
    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return L.Msg("GSTIN is required.");
        }

        var gstin = Normalize(value);
        if (gstin.Length != 15)
        {
            return L.Msg("GSTIN must be exactly 15 characters.");
        }

        if (!FormatRegex().IsMatch(gstin))
        {
            return L.Msg("GSTIN format is invalid. Expected 2-digit state code, 10-character PAN, entity number, 'Z' and a check character.");
        }

        if (ComputeCheckCharacter(gstin.AsSpan(0, 14)) != gstin[14])
        {
            return L.Msg("GSTIN check character doesn't match. Please re-check the number for typos.");
        }

        return null;
    }

    public static bool IsValid(string? value) => Validate(value) is null;

    /// <summary>The two-digit GST state code (first two characters).</summary>
    public static string StateCode(string gstin) => Normalize(gstin)[..2];

    /// <summary>The PAN embedded in characters 3-12.</summary>
    public static string Pan(string gstin) => Normalize(gstin).Substring(2, 10);

    /// <summary>GSTN's mod-36 check character over the first 14 characters.</summary>
    public static char ComputeCheckCharacter(ReadOnlySpan<char> first14)
    {
        if (first14.Length != 14)
        {
            throw new ArgumentException("Exactly 14 characters are required.", nameof(first14));
        }

        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var value = Charset.IndexOf(first14[i], StringComparison.Ordinal);
            if (value < 0)
            {
                throw new ArgumentException($"'{first14[i]}' is not a valid GSTIN character.", nameof(first14));
            }

            // Factors alternate 1, 2, 1, 2...; each product contributes its base-36 "digits".
            var product = value * (i % 2 == 0 ? 1 : 2);
            sum += (product / 36) + (product % 36);
        }

        return Charset[(36 - (sum % 36)) % 36];
    }
}
