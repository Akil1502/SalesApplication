using System.Globalization;
using System.Text;

namespace SalesApp.AI.AskData;

/// <summary>
/// Turns a query's column alias into a header people read: "SalesValue" becomes "Sales value", "Sku" becomes
/// "SKU". Claude is asked for aliases like SalesValue because they need no quoting in SQL; the table and the
/// Excel file show this label instead.
/// </summary>
public static class ColumnLabel
{
    private static readonly Dictionary<string, string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sku"] = "SKU", ["gst"] = "GST", ["gstin"] = "GSTIN", ["hsn"] = "HSN", ["cgst"] = "CGST", ["sgst"] = "SGST",
        ["igst"] = "IGST", ["utgst"] = "UTGST", ["id"] = "ID", ["fy"] = "FY", ["upi"] = "UPI", ["neft"] = "NEFT",
        ["rtgs"] = "RTGS", ["b2b"] = "B2B", ["b2c"] = "B2C", ["po"] = "PO", ["utc"] = "UTC", ["yoy"] = "YoY",
    };

    public static string From(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        // An alias that already reads as words (Claude quoted it, e.g. [Sales value]) is left alone.
        if (name.Contains(' ', StringComparison.Ordinal)) return name.Trim();
        if (Acronyms.TryGetValue(name, out var whole)) return whole;

        var words = Split(name);
        if (words.Count == 0) return name;
        var label = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (i > 0) label.Append(' ');
            if (Acronyms.TryGetValue(word, out var acronym)) label.Append(acronym);
            else if (word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c))) label.Append(word);
            else if (i == 0) label.Append(char.ToUpper(word[0], CultureInfo.InvariantCulture)).Append(word[1..]);
            else label.Append(word.ToLowerInvariant());
        }
        return label.ToString();
    }

    /// <summary>Splits at underscores and at case changes, keeping runs of capitals (GSTAmount → GST, Amount) together.</summary>
    private static List<string> Split(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '_' or '-')
            {
                Flush();
                continue;
            }
            if (current.Length > 0)
            {
                var previous = name[i - 1];
                var startsWord = char.IsUpper(c) && (char.IsLower(previous) || char.IsDigit(previous))
                    || char.IsUpper(c) && char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1])
                    || char.IsDigit(c) && char.IsLetter(previous)
                    || char.IsLetter(c) && char.IsDigit(previous);
                if (startsWord) Flush();
            }
            current.Append(c);
        }
        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0) words.Add(current.ToString());
            current.Clear();
        }
    }
}
