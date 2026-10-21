using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SalesApp.Core.Localization;

/// <summary>
/// English or Tamil for text the server writes: messages, labels, notifications, report headings. The English is the
/// key: Localization/ta.json maps "Customer not found." to its Tamil, and a template such as "Invoice {0} is
/// cancelled." matches any invoice number, so a message built with string interpolation (or saved in English
/// earlier, like a notification) still reads in Tamil.
/// <para>
/// Text is written in English everywhere and translated once, where it leaves the server: <see cref="T"/> uses the
/// request's language (Accept-Language, set by the API for each request). Exception messages and DataAnnotations
/// messages are found by the translation tests on their own; mark any other text the server shows with
/// <see cref="Msg"/> so the tests find it too.
/// </para>
/// </summary>
public static partial class L
{
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-IN");
    public static readonly CultureInfo Tamil = CultureInfo.GetCultureInfo("ta-IN");

    private static readonly Lazy<Catalog> TamilCatalog = new(Catalog.Load);

    /// <summary>Whether this request (or this piece of work) is in Tamil.</summary>
    public static bool InTamil => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ta";

    /// <summary>The Tamil texts keyed by English, for the translation tests and the review sheet.</summary>
    public static IReadOnlyDictionary<string, string> TamilTexts => TamilCatalog.Value.Texts;

    /// <summary>Marks text the server shows so the translation tests collect it; returns it unchanged.</summary>
    public static string Msg(string english) => english;

    /// <summary>Text built for this request and sent straight back: marked like <see cref="Msg"/>, translated like <see cref="T"/>.</summary>
    public static string Show(string english) => T(english);

    /// <summary>English text in the language of this request.</summary>
    [return: NotNullIfNotNull(nameof(english))]
    public static string? T(string? english) => english is null ? null : InTamil ? TamilCatalog.Value.Translate(english) : english;

    /// <summary>
    /// A value that may be one of the app's words (Yes, Added, Partially paid) or data (a name, an amount): only an
    /// exact match is translated, never a template.
    /// </summary>
    public static string? Word(string? value) =>
        value is not null && InTamil && TamilCatalog.Value.Texts.TryGetValue(value, out var tamil) ? tamil : value;

    /// <summary>A report cell: one of the app's words, or a month ("Oct 2026"), in Tamil; anything else as it is.</summary>
    public static string Value(string value) => InTamil ? TamilCatalog.Value.TranslateValueOnly(value) : value;

    /// <summary>English text in Tamil, whatever the request's language (a Tamil printed document, a both-language field).</summary>
    public static string ToTamil(string english) => TamilCatalog.Value.Translate(english);

    private sealed partial class Catalog
    {
        private readonly FrozenDictionary<string, string> texts;
        private readonly Template[] templates;
        private readonly ConcurrentDictionary<string, string> recent = new(StringComparer.Ordinal);

        private Catalog(Dictionary<string, string> texts)
        {
            this.texts = texts.ToFrozenDictionary(StringComparer.Ordinal);
            templates = texts
                .Where(pair => Placeholder().IsMatch(pair.Key))
                .Select(pair => Template.Create(pair.Key, pair.Value))
                .Where(template => template is not null)
                .Select(template => template!)
                // The most specific first: more fixed words means a closer match.
                .OrderByDescending(template => template.FixedLength)
                .ToArray();
        }

        public FrozenDictionary<string, string> Texts => texts;

        public static Catalog Load()
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream("SalesApp.Core.Localization.ta.json")
                ?? throw new InvalidOperationException("The Tamil texts (Localization/ta.json) aren't embedded in SalesApp.Core.");
            return new Catalog(JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? []);
        }

        public string Translate(string english)
        {
            if (english.Length == 0)
            {
                return english;
            }

            if (texts.TryGetValue(english, out var tamil))
            {
                return tamil;
            }

            if (recent.TryGetValue(english, out var cached))
            {
                return cached;
            }

            var translated = Match(english) ?? BySentence(english) ?? english;
            // Messages repeat (the same notifications every minute), so remember a few thousand.
            if (recent.Count > 5_000)
            {
                recent.Clear();
            }

            recent[english] = translated;
            return translated;
        }

        private string? Match(string english)
        {
            foreach (var template in templates)
            {
                var values = template.Match(english);
                if (values is not null)
                {
                    return template.Fill(values.Select(TranslateValue).ToArray());
                }
            }

            return null;
        }

        /// <summary>A message joined from sentences ("X can't be cancelled. " + "Issue a credit note instead.").</summary>
        private string? BySentence(string english)
        {
            var sentences = SentenceEnd().Split(english).Where(s => s.Length > 0).ToArray();
            if (sentences.Length < 2)
            {
                return null;
            }

            var parts = sentences.Select(s => texts.TryGetValue(s, out var t) ? t : Match(s)).ToArray();
            return parts.All(p => p is not null) ? string.Join(' ', parts) : null;
        }

        public string TranslateValueOnly(string value) =>
            texts.TryGetValue(value, out var tamil) ? tamil : EnglishMonth().Replace(value, m => TamilMonths[m.Value]);

        /// <summary>
        /// A value filled into a template: a word or sentence the server wrote (a payment mode, a reason built
        /// elsewhere) is translated in turn, and an English date names its month in Tamil. Names, numbers and codes
        /// stay as they are.
        /// </summary>
        private string TranslateValue(string value)
        {
            if (texts.TryGetValue(value, out var tamil))
            {
                return tamil;
            }

            if (value.Length > 3 && value[^1] is '.' or '?' or '!' && value.Contains(' ', StringComparison.Ordinal))
            {
                return Match(value) ?? value;
            }

            return EnglishMonth().Replace(value, m => TamilMonths[m.Value]);
        }

        [GeneratedRegex(@"\{\d+\}")]
        private static partial Regex Placeholder();

        [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z₹])")]
        private static partial Regex SentenceEnd();

        [GeneratedRegex(@"\b(January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\b(?=\s+\d{4}\b)")]
        private static partial Regex EnglishMonth();

        private static readonly Dictionary<string, string> TamilMonths = new(StringComparer.Ordinal)
        {
            ["January"] = "ஜனவரி", ["February"] = "பிப்ரவரி", ["March"] = "மார்ச்", ["April"] = "ஏப்ரல்", ["May"] = "மே",
            ["June"] = "ஜூன்", ["July"] = "ஜூலை", ["August"] = "ஆகஸ்ட்", ["September"] = "செப்டம்பர்", ["October"] = "அக்டோபர்",
            ["November"] = "நவம்பர்", ["December"] = "டிசம்பர்",
            ["Jan"] = "ஜன.", ["Feb"] = "பிப்.", ["Mar"] = "மார்.", ["Apr"] = "ஏப்.", ["Jun"] = "ஜூன்", ["Jul"] = "ஜூலை",
            ["Aug"] = "ஆக.", ["Sep"] = "செப்.", ["Sept"] = "செப்.", ["Oct"] = "அக்.", ["Nov"] = "நவ.", ["Dec"] = "டிச.",
        };
    }

    /// <summary>"Invoice {0} is cancelled." as a pattern that finds the {0} in a real message.</summary>
    private sealed class Template
    {
        private readonly Regex pattern;
        private readonly string tamil;
        private readonly string longestFixed;

        private Template(Regex pattern, string tamil, string longestFixed, int fixedLength)
        {
            this.pattern = pattern;
            this.tamil = tamil;
            this.longestFixed = longestFixed;
            FixedLength = fixedLength;
        }

        public int FixedLength { get; }

        public static Template? Create(string english, string tamil)
        {
            var pieces = Regex.Split(english, @"(\{\d+\})");
            var fixedParts = pieces.Where((_, i) => i % 2 == 0).ToArray();
            var fixedLength = fixedParts.Sum(p => p.Trim().Length);
            // A template that's all placeholders ("{0} {1}") would match anything.
            if (fixedLength < 3)
            {
                return null;
            }

            var regex = string.Concat(pieces.Select((piece, i) => i % 2 == 0 ? Regex.Escape(piece) : $"(?<p{piece[1..^1]}>.+?)"));
            return new Template(
                new Regex($"^{regex}$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)),
                tamil,
                fixedParts.MaxBy(p => p.Length)!,
                fixedLength);
        }

        public string[]? Match(string english)
        {
            if (!english.Contains(longestFixed, StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                var match = pattern.Match(english);
                if (!match.Success)
                {
                    return null;
                }

                var count = pattern.GetGroupNames().Count(n => n.StartsWith('p'));
                return Enumerable.Range(0, count).Select(i => match.Groups[$"p{i}"].Value).ToArray();
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        public string Fill(string[] values) =>
            Regex.Replace(tamil, @"\{(\d+)\}", m => int.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var i) && i < values.Length ? values[i] : m.Value);
    }
}
