using System.Globalization;
using System.Text;

namespace SalesApp.AI.OrderEntry;

public static class AliasText
{
    public const int MaxLength = 120;

    /// <summary>
    /// Lower case, letters and digits only, single spaces: "30S  Combed (white)!" -> "30s combed white". Two wordings
    /// that differ only in case, punctuation or spacing are the same alias.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (IsWordCharacter(ch))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(ch));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.Length <= MaxLength ? builder.ToString() : builder.ToString(0, MaxLength).TrimEnd();
    }

    /// <summary>Letters, digits, and the vowel signs of Indic scripts, which .NET classes as marks rather than letters.</summary>
    private static bool IsWordCharacter(char ch) =>
        char.IsLetterOrDigit(ch) || CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
}
