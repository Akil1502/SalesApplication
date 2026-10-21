#:package ClosedXML@0.105.1

// The Tamil review sheet. Run from the repo root:
//   dotnet run scripts/translations.cs -- export [file.xlsx]   writes every text, English beside Tamil (default:
//                                                              translations-review/SalesApp-Tamil-review.xlsx)
//   dotnet run scripts/translations.cs -- import <file.xlsx>   takes the reviewer's corrections back into both ta.json
// The reviewer only types in the "Correction" column (and "Comment" for questions); everything else stays as it is.
// Needs Node for the frontend's list of where each text appears (node frontend/scripts/i18n.ts --where).

using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

var root = Directory.GetCurrentDirectory();
var appCatalog = Path.Combine(root, "frontend", "src", "locales", "ta.json");
var serverCatalog = Path.Combine(root, "backend", "src", "SalesApp.Core", "Localization", "ta.json");
if (!File.Exists(appCatalog) || !File.Exists(serverCatalog))
{
    Console.Error.WriteLine("Run this from the repo root (the folder with frontend/ and backend/).");
    return 1;
}

var command = args.FirstOrDefault();
return command switch
{
    "export" => Export(args.ElementAtOrDefault(1) ?? Path.Combine(root, "translations-review", "SalesApp-Tamil-review.xlsx")),
    "import" when args.Length > 1 => Import(args[1]),
    _ => Usage(),
};

int Usage()
{
    Console.Error.WriteLine("Usage: dotnet run scripts/translations.cs -- export [file.xlsx] | import <file.xlsx>");
    return 1;
}

int Export(string path)
{
    var app = Read(appCatalog);
    var server = Read(serverCatalog);
    var where = AppWhere();

    using var book = new XLWorkbook();
    ReadMe(book.AddWorksheet("Read me"));
    Glossary(book.AddWorksheet("Glossary"));

    var sheet = book.AddWorksheet("Translations");
    string[] headings = ["Id", "Where", "English", "Tamil now", "Correction", "Comment"];
    for (var c = 0; c < headings.Length; c++)
    {
        sheet.Cell(1, c + 1).Value = headings[c];
    }

    var row = 2;
    void Add(string id, string place, string english, string tamil)
    {
        sheet.Cell(row, 1).Value = id;
        sheet.Cell(row, 2).Value = place;
        sheet.Cell(row, 3).Value = english;
        sheet.Cell(row, 4).Value = tamil;
        row++;
    }

    foreach (var (key, value) in app.OrderBy(p => Place(where, p.Key), StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal))
    {
        var english = KeyText(key);
        var place = Place(where, key);
        if (value is JsonObject forms)
        {
            var one = where.TryGetPropertyValue(key, out var info) && info?["one"] is { } singular ? singular.GetValue<string>() : english;
            Add($"app|{key}|one", place, one, forms["one"]?.GetValue<string>() ?? "");
            Add($"app|{key}|other", place, english, forms["other"]?.GetValue<string>() ?? "");
        }
        else
        {
            Add($"app|{key}", place, english, value?.GetValue<string>() ?? "");
        }
    }

    foreach (var (key, value) in server.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        Add($"server|{key}", "Server (messages, notifications, reports, audit trail)", key, value?.GetValue<string>() ?? "");
    }

    var header = sheet.Row(1);
    header.Style.Font.Bold = true;
    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EAF6");
    sheet.SheetView.FreezeRows(1);
    sheet.Range(1, 1, row - 1, headings.Length).SetAutoFilter();
    sheet.Column(1).Width = 14;
    sheet.Column(1).Style.Font.FontColor = XLColor.Gray;
    sheet.Column(2).Width = 34;
    sheet.Column(3).Width = 60;
    sheet.Column(4).Width = 60;
    sheet.Column(5).Width = 60;
    sheet.Column(6).Width = 30;
    sheet.Columns(2, 6).Style.Alignment.WrapText = true;
    sheet.Columns(1, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    // Tamil letters need a font that has them; Windows has Nirmala UI.
    sheet.Columns(4, 5).Style.Font.FontName = "Nirmala UI";
    sheet.Column(5).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFDE7");
    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EAF6");

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    book.SaveAs(path);
    Console.WriteLine($"Wrote {row - 2} texts to {path}");
    return 0;
}

int Import(string path)
{
    var app = Read(appCatalog);
    var server = Read(serverCatalog);
    using var book = new XLWorkbook(path);
    var sheet = book.Worksheet("Translations");

    var changed = 0;
    var problems = new List<string>();
    foreach (var row in sheet.RowsUsed().Skip(1))
    {
        var correction = row.Cell(5).GetString().Trim();
        if (correction.Length == 0)
        {
            continue;
        }

        var id = row.Cell(1).GetString();
        var english = row.Cell(3).GetString();
        var parts = id.Split('|');
        if (parts.Length < 2 || parts[0] is not ("app" or "server"))
        {
            problems.Add($"Row {row.RowNumber()}: the Id column was changed ({id}).");
            continue;
        }

        if (Placeholders(english) != Placeholders(correction))
        {
            problems.Add($"Row {row.RowNumber()}: keep every {{…}} from the English ({Placeholders(english)}); skipped: {correction}");
            continue;
        }

        var catalog = parts[0] == "app" ? app : server;
        var key = parts[1];
        if (!catalog.ContainsKey(key))
        {
            problems.Add($"Row {row.RowNumber()}: the app no longer has this text; skipped: {english}");
            continue;
        }

        if (parts.Length == 3 && catalog[key] is JsonObject forms)
        {
            forms[parts[2]] = correction;
        }
        else
        {
            catalog[key] = correction;
        }

        changed++;
    }

    Write(appCatalog, app);
    Write(serverCatalog, server);
    Console.WriteLine($"Took {changed} correction(s) into the Tamil texts.");
    foreach (var problem in problems)
    {
        Console.WriteLine(problem);
    }

    Console.WriteLine("Next: run the tests (npm test in frontend, dotnet test in backend) and look at the screens in Tamil.");
    return problems.Count == 0 ? 0 : 2;
}

static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

static void Write(string path, JsonObject catalog)
{
    var sorted = new JsonObject();
    foreach (var (key, value) in catalog.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        sorted[key] = value?.DeepClone();
    }

    var options = new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    File.WriteAllText(path, sorted.ToJsonString(options) + "\n");
}

static string Placeholders(string text) =>
    string.Join(",", Regex.Matches(text, @"\{\w+\}").Select(m => m.Value).Order(StringComparer.Ordinal));

/// <summary>A context key ("status::Open") shows its English; the context goes in the Where column.</summary>
static string KeyText(string key) => key.Contains("::", StringComparison.Ordinal) ? key[(key.IndexOf("::", StringComparison.Ordinal) + 2)..] : key;

static string Place(JsonObject where, string key)
{
    var context = key.Contains("::", StringComparison.Ordinal) ? $" (as: {key[..key.IndexOf("::", StringComparison.Ordinal)]})" : "";
    if (!where.TryGetPropertyValue(key, out var info) || info?["where"] is not JsonArray files || files.Count == 0)
    {
        return "Screen" + context;
    }

    var first = files[0]!.GetValue<string>();
    var screen = Regex.Replace(first, @"^(features|components)/|:\d+$|\.tsx?$", "");
    return (files.Count > 1 ? $"Screen: {screen} (+{files.Count - 1} more)" : $"Screen: {screen}") + context;
}

JsonObject AppWhere()
{
    try
    {
        var start = new ProcessStartInfo("node", "scripts/i18n.ts --where")
        {
            WorkingDirectory = Path.Combine(root, "frontend"),
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var node = Process.Start(start)!;
        var json = node.StandardOutput.ReadToEnd();
        node.WaitForExit();
        return JsonNode.Parse(json)!.AsObject();
    }
    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or JsonException)
    {
        Console.Error.WriteLine("Couldn't list where each text appears (is Node installed?); the Where column says only Screen.");
        return [];
    }
}

void ReadMe(IXLWorksheet sheet)
{
    string[] lines =
    [
        "SalesApp: Tamil translation review",
        "",
        "Thank you for checking the Tamil. Everything the app shows is on the Translations sheet: the English, the Tamil",
        "it uses now, and where it appears.",
        "",
        "How to review",
        "1. Read each row's English and 'Tamil now'.",
        "2. If the Tamil is wrong or unnatural, type the better Tamil in the 'Correction' column. Leave it empty when the",
        "   Tamil is fine. Use 'Comment' for questions.",
        "3. Keep every {…} exactly as in the English, such as {name}, {count} or {0}: the app puts a name, a number or a",
        "   document number there. You may move it within the sentence.",
        "4. Keep GST, GSTIN, HSN, SKU, UPI, PO, PDF and other such terms in English letters, as the Glossary says.",
        "5. Don't change the Id, Where, English or 'Tamil now' columns.",
        "",
        "The Glossary sheet lists the words used for the main business terms. If you'd change one of them, say so in a",
        "comment on one row; it will be changed everywhere.",
        "",
        "Rows marked Server are messages, notifications, report headings and audit-trail words written by the server.",
        "An 'app' row with (one) and (other) is the singular and plural of the same text.",
    ];
    for (var i = 0; i < lines.Length; i++)
    {
        sheet.Cell(i + 1, 1).Value = lines[i];
    }

    sheet.Cell(1, 1).Style.Font.Bold = true;
    sheet.Cell(1, 1).Style.Font.FontSize = 14;
    sheet.Column(1).Width = 120;
}

void Glossary(IXLWorksheet sheet)
{
    sheet.Cell(1, 1).Value = "English";
    sheet.Cell(1, 2).Value = "Tamil";
    sheet.Row(1).Style.Font.Bold = true;
    var guide = Path.Combine(root, "docs", "TRANSLATION.md");
    var rows = File.Exists(guide)
        ? File.ReadLines(guide).SkipWhile(l => !l.StartsWith("## Glossary", StringComparison.Ordinal))
            .Where(l => l.StartsWith('|') && !l.StartsWith("|---", StringComparison.Ordinal) && !l.StartsWith("| English", StringComparison.Ordinal))
        : [];
    var r = 2;
    foreach (var line in rows)
    {
        var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
        if (cells.Length >= 2)
        {
            sheet.Cell(r, 1).Value = cells[0];
            sheet.Cell(r, 2).Value = cells[1];
            r++;
        }
    }

    sheet.Column(1).Width = 40;
    sheet.Column(2).Width = 60;
    sheet.Column(2).Style.Font.FontName = "Nirmala UI";
}
