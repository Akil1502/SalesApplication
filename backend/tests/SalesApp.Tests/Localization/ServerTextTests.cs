using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using SalesApp.Core.Localization;
using SalesApp.Infrastructure.Auditing;

namespace SalesApp.Tests.Localization;

/// <summary>
/// Every message and label the server writes has Tamil in SalesApp.Core/Localization/ta.json (see
/// <see cref="ServerTextScanner"/> for what counts). A failure lists what's missing; the full list is also written
/// to missing-server-texts.json next to the test binaries.
/// </summary>
public sealed partial class ServerTextTests
{
    private static readonly Lazy<(List<ServerTextScanner.Found> Found, List<ServerTextScanner.Glued> Glued)> Scanned = new(() =>
    {
        var (found, glued) = ServerTextScanner.Scan();
        // The audit trail's field names are made from property names, so they aren't in the source as text.
        found.AddRange(AuditFormat.TextsForTranslation().Select(text => new ServerTextScanner.Found(text, "audit trail field names", 0)));
        return (found, glued);
    });
    private static readonly JsonSerializerOptions Readable = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public void Every_server_text_has_Tamil()
    {
        var missing = Scanned.Value.Found
            .Where(f => !L.TamilTexts.ContainsKey(f.Template))
            .GroupBy(f => f.Template)
            .ToDictionary(g => g.Key, g => g.Select(f => $"{f.File}:{f.Line}").ToArray());

        if (missing.Count > 0)
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "missing-server-texts.json"),
                JsonSerializer.Serialize(missing, Readable));
        }

        None(missing.Select(m => $"{m.Key}  ({m.Value[0]})"));
    }

    [Fact]
    public void Tamil_keeps_every_placeholder()
    {
        var wrong = L.TamilTexts
            .Where(pair => Placeholders(pair.Key) != Placeholders(pair.Value))
            .Select(pair => $"{pair.Key}  →  {pair.Value}");
        None(wrong);
    }

    [Fact]
    public void Tamil_has_nothing_the_server_no_longer_says()
    {
        var used = Scanned.Value.Found.Select(f => f.Template).ToHashSet(StringComparer.Ordinal);
        None(L.TamilTexts.Keys.Where(key => !used.Contains(key)));
    }

    [Fact]
    public void Messages_are_whole_sentences()
    {
        // $"{n} {(n == 1 ? "line" : "lines")}" leaves "line" in English: write one sentence per case instead.
        None(Scanned.Value.Glued.Select(g => $"{g.Template}  ({g.File}:{g.Line})"));
    }

    [Fact]
    public void A_message_built_with_values_reads_in_Tamil()
    {
        var english = L.TamilTexts.Keys.First(key => key.Contains("{0}", StringComparison.Ordinal) && !key.StartsWith("{0}", StringComparison.Ordinal));
        var message = english.Replace("{0}", "INV/26-27/0042", StringComparison.Ordinal);
        message = Regex.Replace(message, @"\{\d+\}", "Sri Murugan Textiles");

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = L.Tamil;
            var tamil = L.T(message);
            Assert.NotEqual(message, tamil);
            Assert.Contains("INV/26-27/0042", tamil, StringComparison.Ordinal);

            CultureInfo.CurrentUICulture = L.English;
            Assert.Equal(message, L.T(message));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>Fails with every problem listed, one per line (xUnit would cut a collection short).</summary>
    private static void None(IEnumerable<string> problems)
    {
        var list = problems.ToList();
        if (list.Count > 0)
        {
            Assert.Fail($"{list.Count}:{Environment.NewLine}{string.Join(Environment.NewLine, list)}");
        }
    }

    private static string Placeholders(string text) => string.Join(',', PlaceholderPattern().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal));

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
