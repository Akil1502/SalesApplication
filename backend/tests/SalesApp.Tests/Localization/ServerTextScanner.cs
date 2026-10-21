using System.Text;
using System.Text.RegularExpressions;

namespace SalesApp.Tests.Localization;

/// <summary>
/// Reads the backend's source for text the server shows, as the templates <c>L.T</c> matches: an interpolated
/// <c>$"Invoice {invoice.Number} is cancelled."</c> becomes <c>"Invoice {0} is cancelled."</c>, and pieces joined
/// with + become one template. It collects the messages of the app's exceptions, DataAnnotations
/// <c>ErrorMessage</c>s and anything marked with <c>L.Msg(...)</c>; each branch of a ?: is its own text.
/// </summary>
public static partial class ServerTextScanner
{
    public sealed record Found(string Template, string File, int Line);

    /// <summary>Text with words chosen inside a {hole}: it can't be translated as one sentence.</summary>
    public sealed record Glued(string Template, string File, int Line);

    private const char Hole = '\u0001';

    public static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SalesApp.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the backend folder (SalesApp.slnx).");
    }

    public static (List<Found> Found, List<Glued> Glued) Scan()
    {
        var src = Path.Combine(BackendRoot(), "src");
        var found = new List<Found>();
        var glued = new List<Glued>();
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/Migrations/", StringComparison.Ordinal))
            {
                continue;
            }

            ScanFile(File.ReadAllText(path), relative, found, glued);
        }

        return (found, glued);
    }

    public static void ScanFile(string code, string file, List<Found> found, List<Glued> glued)
    {
        foreach (Match m in Starts().Matches(code))
        {
            var open = m.Index + m.Length - 1;
            var close = Close(code, open);
            if (close < 0)
            {
                continue;
            }

            var isValidation = m.Groups["type"].Value == "ValidationException";
            foreach (var (template, at, isGlued) in Arguments(code, open + 1, close))
            {
                // Not text: a field name first in ValidationException("creditLimit", "..."), a path such as "/api".
                if ((isValidation && FieldName().IsMatch(template)) || template.StartsWith('/') || !Regex.IsMatch(template, "[A-Za-z]{2}"))
                {
                    continue;
                }

                found.Add(new Found(template, file, LineOf(code, at)));
                if (isGlued)
                {
                    glued.Add(new Glued(template, file, LineOf(code, at)));
                }
            }
        }

        foreach (Match m in ErrorMessages().Matches(code))
        {
            found.Add(new Found(Regex.Unescape(m.Groups["text"].Value), file, LineOf(code, m.Index)));
        }
    }

    private static IEnumerable<(string Template, int At, bool Glued)> Arguments(string code, int start, int end)
    {
        foreach (var (argStart, argEnd) in Split(code, start, end, isComma: true))
        {
            foreach (var (s, e) in Split(code, argStart, argEnd, isComma: false))
            {
                foreach (var template in Segment(code, s, e))
                {
                    yield return template;
                }
            }
        }
    }

    /// <summary>One branch of an argument: string pieces joined by + are one template; calls are looked into.</summary>
    private static IEnumerable<(string Template, int At, bool Glued)> Segment(string code, int start, int end)
    {
        var run = new StringBuilder();
        var runAt = -1;
        var hasString = false;
        var glued = false;
        var afterPlus = true;
        var nested = new List<(string, int, bool)>();

        (string, int, bool)? Flush()
        {
            (string, int, bool)? result = hasString ? (Number(run.ToString()), runAt, glued) : null;
            run.Clear();
            hasString = false;
            glued = false;
            return result;
        }

        var i = start;
        while (i < end)
        {
            var c = code[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '+')
            {
                afterPlus = true;
                i++;
                continue;
            }

            if (!afterPlus && Flush() is { } done)
            {
                yield return done;
            }

            if (run.Length == 0)
            {
                runAt = i;
            }

            if (IsStringStart(code, i))
            {
                i = ReadString(code, i, run, ref glued);
                hasString = true;
            }
            else
            {
                var tokenEnd = Token(code, i, end);
                // Look inside calls and brackets for text of their own (string.Create(…, $"…")).
                for (var j = i; j < tokenEnd; j++)
                {
                    if (code[j] is '(' or '[')
                    {
                        var close = Close(code, j);
                        if (close < 0 || close > tokenEnd)
                        {
                            break;
                        }

                        nested.AddRange(Arguments(code, j + 1, close));
                        j = close;
                    }
                    else if (IsStringStart(code, j))
                    {
                        j = SkipString(code, j);
                    }
                }

                run.Append(Hole);
                i = Math.Max(tokenEnd, i + 1);
            }

            afterPlus = false;
        }

        if (Flush() is { } last)
        {
            yield return last;
        }

        foreach (var n in nested)
        {
            yield return n;
        }
    }

    private static string Number(string template)
    {
        var n = 0;
        var result = new StringBuilder();
        foreach (var c in template)
        {
            if (c == Hole)
            {
                result.Append('{').Append(n++).Append('}');
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>Splits at top-level commas, or (isComma: false) at the ? and : of a conditional and at ??.</summary>
    private static List<(int Start, int End)> Split(string code, int start, int end, bool isComma)
    {
        var parts = new List<(int, int)>();
        var depth = 0;
        var from = start;
        for (var i = start; i < end; i++)
        {
            var c = code[i];
            if (IsStringStart(code, i))
            {
                i = SkipString(code, i) - 1;
                continue;
            }

            switch (c)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case '\'':
                    i = SkipChar(code, i);
                    break;
                case ',' when isComma && depth == 0:
                    parts.Add((from, i));
                    from = i + 1;
                    break;
                case '?' when !isComma && depth == 0 && code[i + 1] is not '.' and not '[':
                    parts.Add((from, i));
                    from = code[i + 1] == '?' ? i + 2 : i + 1;
                    if (code[i + 1] == '?')
                    {
                        i++;
                    }

                    break;
                case ':' when !isComma && depth == 0:
                    parts.Add((from, i));
                    from = i + 1;
                    break;
            }
        }

        parts.Add((from, end));
        return parts;
    }

    /// <summary>A name, member chain or call, or a bracketed group.</summary>
    private static int Token(string code, int i, int end)
    {
        if (code[i] is '(' or '[')
        {
            var close = Close(code, i);
            return close < 0 ? end : close + 1;
        }

        if (!(char.IsLetterOrDigit(code[i]) || code[i] is '_' or '@'))
        {
            return i + 1;
        }

        while (i < end && (char.IsLetterOrDigit(code[i]) || code[i] is '_' or '.' or '!' or '@'))
        {
            i++;
            if (i < end && code[i] is '(' or '[')
            {
                var close = Close(code, i);
                if (close < 0)
                {
                    return end;
                }

                i = close + 1;
            }
        }

        return i;
    }

    private static bool IsStringStart(string code, int i) =>
        code[i] == '"' || (code[i] is '$' or '@' && i + 1 < code.Length && (code[i + 1] == '"' || (code[i + 1] is '$' or '@' && i + 2 < code.Length && code[i + 2] == '"')));

    /// <summary>Reads a "…", @"…" or $"…" literal into the template (holes as markers); returns the index after it.</summary>
    private static int ReadString(string code, int i, StringBuilder template, ref bool glued)
    {
        var interpolated = false;
        var verbatim = false;
        while (code[i] is '$' or '@')
        {
            interpolated |= code[i] == '$';
            verbatim |= code[i] == '@';
            i++;
        }

        i++; // the opening quote
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '"')
            {
                if (verbatim && code[i + 1] == '"')
                {
                    template.Append('"');
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            if (!verbatim && c == '\\')
            {
                template.Append(code[i + 1] switch { 'n' => '\n', 't' => '\t', var other => other });
                i += 2;
                continue;
            }

            if (interpolated && c == '{')
            {
                if (code[i + 1] == '{')
                {
                    template.Append('{');
                    i += 2;
                    continue;
                }

                var close = Close(code, i);
                // Words inside a hole ({n == 1 ? "line" : "lines"}) stay English in a Tamil sentence.
                // (A format string such as ToString("dd MMM yyyy") is fine: only a choice between words is glued.)
                glued |= Regex.IsMatch(code[(i + 1)..close], "[?:]\\s*\"[^\"]*[A-Za-z]{2}[^\"]*\"");
                template.Append(Hole);
                i = close + 1;
                continue;
            }

            if (interpolated && c == '}' && code[i + 1] == '}')
            {
                template.Append('}');
                i += 2;
                continue;
            }

            template.Append(c);
            i++;
        }

        return i;
    }

    private static int SkipString(string code, int i)
    {
        var ignored = new StringBuilder();
        var glued = false;
        return ReadString(code, i, ignored, ref glued);
    }

    private static int SkipChar(string code, int i) => code.IndexOf('\'', i + (code[i + 1] == '\\' ? 3 : 2));

    /// <summary>The bracket that closes the one at <paramref name="open"/>, stepping over strings.</summary>
    private static int Close(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (i > open && IsStringStart(code, i))
            {
                i = SkipString(code, i) - 1;
                continue;
            }

            switch (code[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
                case '\'':
                    i = SkipChar(code, i);
                    break;
            }
        }

        return -1;
    }

    private static int LineOf(string code, int index) => code.AsSpan(0, Math.Max(0, index)).Count('\n') + 1;

    [GeneratedRegex(@"(?:new\s+(?<type>NotFoundException|ConflictException|ForbiddenException|ValidationException|AiUnavailableException|AiBudgetExceededException|AiRefusedException|AiServiceException|ApiError)|\bL\.(?:Msg|Show))\s*\(")]
    private static partial Regex Starts();

    [GeneratedRegex(@"ErrorMessage\s*=\s*""(?<text>(?:[^""\\]|\\.)*)""")]
    private static partial Regex ErrorMessages();

    [GeneratedRegex(@"^[a-z][A-Za-z0-9.\[\]{}]*$")]
    private static partial Regex FieldName();
}
