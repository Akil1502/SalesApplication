using System.Text.Json;

namespace SalesApp.AI.AskData;

/// <summary>How a query result is shown to Claude: the columns, the row count and a sample of the rows.</summary>
public static class QueryResultJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string ForClaude(string? resultId, QueryResult result) => JsonSerializer.Serialize(new
    {
        resultId,
        columns = result.Columns.Select(c => c.Name),
        rowCount = result.Rows.Count,
        truncated = result.Truncated,
        rowsShown = Math.Min(result.Rows.Count, AskDataPrompt.RowsShownToClaude),
        rows = result.Rows.Take(AskDataPrompt.RowsShownToClaude),
    }, Options);
}
