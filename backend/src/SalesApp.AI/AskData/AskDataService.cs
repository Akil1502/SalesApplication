using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SalesApp.AI.Claude;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.AI.AskData;

/// <summary>Settings for answering data questions.</summary>
public sealed class AskDataOptions
{
    public const string SectionName = "Ai:AskData";

    /// <summary>Most SQL queries Claude may run for one question.</summary>
    public int MaxQueries { get; set; } = 5;
}

/// <summary>A query result kept for a while after the answer, so the user can download it.</summary>
public sealed record StoredResult(Guid UserId, string Question, string Sql, QueryResult Result);

public interface IAskDataService
{
    Task<AskAnswerDto> AskAsync(string question, Func<AskStepDto, Task> onStep, CancellationToken cancellationToken);

    StoredResult? GetResult(Guid resultId);
}

/// <summary>
/// Answers a question in a tool loop: Claude writes SQL, the server checks it (<see cref="SqlGuard"/>), runs it as a
/// reporting-only database user (<see cref="IReadOnlySqlExecutor"/>) and hands back a resultId with a sample of the
/// rows; Claude finishes by naming the resultId that answers the question. The table the user sees is those rows
/// exactly as the database returned them, so the figures on screen never pass through the model.
/// </summary>
public sealed class AskDataService(
    IClaudeGateway claude,
    IGuardedQueryRunner queries,
    ICurrentUser currentUser,
    IBusinessClock clock,
    IMemoryCache cache,
    IOptionsMonitor<AiOptions> aiOptions,
    IOptionsMonitor<AskDataOptions> options) : IAskDataService
{
    public const string Feature = "AskData";

    public StoredResult? GetResult(Guid resultId) =>
        cache.TryGetValue(CacheKey(resultId), out StoredResult? stored) && stored!.UserId == currentUser.UserId ? stored : null;

    public async Task<AskAnswerDto> AskAsync(string question, Func<AskStepDto, Task> onStep, CancellationToken cancellationToken)
    {
        question = question.Trim();
        if (question.Length == 0)
        {
            throw new ValidationException("question", "Type a question.");
        }

        var runId = Guid.NewGuid();
        var manager = currentUser.IsManagerOrAdmin;
        var maxQueries = Math.Clamp(options.CurrentValue.MaxQueries, 1, 10);

        var system = AskDataPrompt.System(manager, maxQueries);
        var messages = new List<ClaudeMessage> { new(false, [new ClaudeTextBlock(AskDataPrompt.Question(clock.Today, question))]) };
        var results = new Dictionary<string, (string Sql, string Purpose, QueryResult Result)>(StringComparer.OrdinalIgnoreCase);
        var usage = new List<ClaudeUsage>();
        var queriesRun = 0;

        for (var turn = 0; turn < maxQueries + 3; turn++)
        {
            await onStep(new AskStepDto(AskStepKind.Thinking, turn == 0 ? "Working out how to answer" : "Looking at the results"));
            var reply = await claude.SendTurnAsync(new ClaudeConversation(Feature, system, AskDataPrompt.Tools, messages), cancellationToken);
            usage.Add(reply.Usage);
            messages.Add(new ClaudeMessage(true, reply.Blocks));

            var toolUses = reply.Blocks.OfType<ClaudeToolUseBlock>().ToList();
            if (toolUses.Count == 0)
            {
                // Claude answered in plain text instead of calling submit_answer: use the text and the last result.
                var text = string.Join("\n", reply.Blocks.OfType<ClaudeTextBlock>().Select(b => b.Text)).Trim();
                var last = results.Keys.LastOrDefault();
                return Finish(runId, question, text, last, results, "none", null, null, [], queriesRun, usage);
            }

            var toolResults = new List<ClaudeBlock>();
            foreach (var toolUse in toolUses)
            {
                if (toolUse.Name == AskDataPrompt.SubmitAnswerTool)
                {
                    var resultId = Text(toolUse.Input, "resultId");
                    if (resultId.Length > 0 && !results.ContainsKey(resultId))
                    {
                        toolResults.Add(new ClaudeToolResultBlock(toolUse.Id, $"There's no result \"{resultId}\". Use a resultId returned by {AskDataPrompt.RunSqlTool}, or \"\".", IsError: true));
                        continue;
                    }

                    var followUps = toolUse.Input.TryGetValue("followUps", out var list) && list.ValueKind == JsonValueKind.Array
                        ? list.EnumerateArray().Select(f => f.ValueKind == JsonValueKind.String ? f.GetString()?.Trim() : null)
                            .Where(f => !string.IsNullOrEmpty(f)).Select(f => f!.Length > 120 ? f[..120] : f).Take(3).ToList()
                        : [];
                    return Finish(runId, question, Text(toolUse.Input, "summary"), resultId.Length > 0 ? resultId : null, results,
                        Text(toolUse.Input, "chartType"), Text(toolUse.Input, "chartX"), Text(toolUse.Input, "chartY"), followUps, queriesRun, usage);
                }

                if (toolUse.Name != AskDataPrompt.RunSqlTool)
                {
                    toolResults.Add(new ClaudeToolResultBlock(toolUse.Id, $"Unknown tool {toolUse.Name}.", IsError: true));
                    continue;
                }

                if (queriesRun >= maxQueries)
                {
                    toolResults.Add(new ClaudeToolResultBlock(toolUse.Id,
                        $"The limit of {maxQueries} queries for one question has been reached. Answer with the results you have.", IsError: true));
                    continue;
                }

                queriesRun++;
                toolResults.Add(await RunQueryAsync(toolUse, runId, question, results, onStep, cancellationToken));
            }

            messages.Add(new ClaudeMessage(false, toolResults));
        }

        throw new AiServiceException("Claude couldn't finish answering within the allowed number of steps. Try a more specific question.", 502);
    }

    private async Task<ClaudeToolResultBlock> RunQueryAsync(
        ClaudeToolUseBlock toolUse, Guid runId, string question,
        Dictionary<string, (string Sql, string Purpose, QueryResult Result)> results,
        Func<AskStepDto, Task> onStep, CancellationToken cancellationToken)
    {
        var sql = Text(toolUse.Input, "sql");
        var purpose = Text(toolUse.Input, "purpose") is { Length: > 0 } p ? p : "Running a query";
        await onStep(new AskStepDto(AskStepKind.Query, purpose, sql));

        var outcome = await queries.RunAsync(runId, question, sql, cancellationToken);
        if (outcome.Outcome == AiQueryOutcome.Rejected)
        {
            await onStep(new AskStepDto(AskStepKind.Rejected, $"Query refused: {outcome.Problem}", sql));
            return new ClaudeToolResultBlock(toolUse.Id, $"Refused by the SQL checker: {outcome.Problem}", IsError: true);
        }

        if (outcome.Result is not { } result)
        {
            await onStep(new AskStepDto(AskStepKind.Failed, $"Query failed: {outcome.Problem}", sql));
            return new ClaudeToolResultBlock(toolUse.Id, $"The database returned an error: {outcome.Problem}", IsError: true);
        }

        var resultId = $"r{results.Count + 1}";
        results[resultId] = (sql, purpose, result);
        await onStep(new AskStepDto(AskStepKind.Rows,
            result.Truncated ? $"More than {ReadOnlySqlExecutor.MaxRows} rows; showing the first {ReadOnlySqlExecutor.MaxRows}" : $"{result.Rows.Count} rows",
            sql, result.Rows.Count));
        return new ClaudeToolResultBlock(toolUse.Id, QueryResultJson.ForClaude(resultId, result));
    }

    private AskAnswerDto Finish(
        Guid runId, string question, string summary, string? resultId,
        Dictionary<string, (string Sql, string Purpose, QueryResult Result)> results,
        string chartType, string? chartX, string? chartY, List<string> followUps, int queriesRun, List<ClaudeUsage> usage)
    {
        summary = summary.Length > 1500 ? summary[..1500] : summary;
        var ai = aiOptions.CurrentValue;
        var total = new AiUsageDto(
            usage.LastOrDefault()?.Model ?? ai.For(Feature).Model,
            usage.Sum(u => u.InputTokens), usage.Sum(u => u.OutputTokens), usage.Sum(u => u.CacheReadTokens), usage.Sum(u => u.CacheWriteTokens),
            usage.Sum(u => u.CostUsd), Math.Round(usage.Sum(u => u.CostUsd) * ai.UsdToInr, 2), usage.Sum(u => u.DurationMs));

        if (resultId is null || !results.TryGetValue(resultId, out var chosen))
        {
            return new AskAnswerDto(runId, question, summary, null, null, null, [], [], false,
                new AskChartDto(AskChartType.None, null, null), followUps, queriesRun, total);
        }

        var stored = Guid.NewGuid();
        cache.Set(CacheKey(stored), new StoredResult(currentUser.UserId, question, chosen.Sql, chosen.Result),
            new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1), Size = chosen.Result.Rows.Count + 1 });

        var columns = chosen.Result.Columns.Select(c => new AskColumnDto(c.Name, ColumnLabel.From(c.Name), (AskColumnKind)(int)c.Kind)).ToList();
        return new AskAnswerDto(runId, question, summary, stored, chosen.Sql, chosen.Purpose, columns, chosen.Result.Rows,
            chosen.Result.Truncated, Chart(columns, chartType, chartX, chartY), followUps, queriesRun, total);
    }

    /// <summary>Keeps a chart only when it names a real category column and a numeric column of the result.</summary>
    private static AskChartDto Chart(List<AskColumnDto> columns, string type, string? x, string? y)
    {
        var chartType = type.ToUpperInvariant() switch { "BAR" => AskChartType.Bar, "LINE" => AskChartType.Line, _ => AskChartType.None };
        var xColumn = columns.FirstOrDefault(c => string.Equals(c.Name, x, StringComparison.OrdinalIgnoreCase));
        var yColumn = columns.FirstOrDefault(c => string.Equals(c.Name, y, StringComparison.OrdinalIgnoreCase));
        var numeric = yColumn?.Kind is AskColumnKind.Money or AskColumnKind.Count or AskColumnKind.Number;
        return chartType == AskChartType.None || xColumn is null || !numeric || xColumn == yColumn
            ? new AskChartDto(AskChartType.None, null, null)
            : new AskChartDto(chartType, xColumn.Name, yColumn!.Name);
    }

    private static string Text(IReadOnlyDictionary<string, JsonElement> input, string name) =>
        input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";

    private static string CacheKey(Guid id) => $"ask-result:{id:N}";
}
