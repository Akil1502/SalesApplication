using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesApp.AI.Claude;
using SalesApp.Core.Contracts.Insights;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Insights;

namespace SalesApp.AI.Insights;

public interface IInsightsService
{
    /// <summary>The most recent run, or null if insights have never been computed.</summary>
    Task<InsightsDto?> GetLatestAsync(CancellationToken cancellationToken);

    Task<InsightsDto> RunAsync(InsightTrigger trigger, CancellationToken cancellationToken);
}

/// <summary>
/// Computes the metrics (always, AI or not), then asks Claude for a written briefing and checks every figure in it
/// against the metrics. A briefing that quotes figures it wasn't given gets one retry with the list of those
/// figures; whatever still doesn't check out is removed. Each run is saved with the exact metrics Claude saw.
/// </summary>
public sealed class InsightsService(
    IInsightMetricsService metrics,
    IClaudeGateway claude,
    AppDbContext db,
    ICurrentUser currentUser,
    TimeProvider time,
    IOptionsMonitor<AiOptions> aiOptions,
    ILogger<InsightsService> logger) : IInsightsService
{
    public const string Feature = "Insights";
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<InsightsDto?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var run = await db.InsightRuns.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        return run is null ? null : ToDto(run);
    }

    public async Task<InsightsDto> RunAsync(InsightTrigger trigger, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var computed = await metrics.ComputeAsync(cancellationToken);
        var metricsJson = JsonSerializer.Serialize(computed, Json);

        var briefing = claude.IsAvailable
            ? await WriteBriefingAsync(computed, metricsJson, cancellationToken)
            : new Briefing(null, NarrativeStatus.AiOff, "AI is switched off or has no API key, so there's no written summary.", 0m);

        var run = new InsightRun
        {
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            Trigger = trigger,
            RequestedByUserId = currentUser.IsSystem ? null : currentUser.UserId,
            MetricsJson = metricsJson,
            NarrativeJson = briefing.Narrative is null ? null : JsonSerializer.Serialize(briefing.Narrative, Json),
            NarrativeStatus = briefing.Status,
            NarrativeNote = briefing.Note is { Length: > 1000 } note ? note[..1000] : briefing.Note,
            CostUsd = briefing.CostUsd,
            DurationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        };
        db.InsightRuns.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return ToDto(run);
    }

    private sealed record Briefing(InsightNarrativeDto? Narrative, NarrativeStatus Status, string? Note, decimal CostUsd);

    private sealed record Reply(string Headline, string Summary, List<ReplyCard> Cards);

    private sealed record ReplyCard(string Kind, string Title, string Body);

    private async Task<Briefing> WriteBriefingAsync(InsightMetricsDto computed, string metricsJson, CancellationToken cancellationToken)
    {
        var company = await db.CompanySettings.AsNoTracking().Select(s => s.TradeName ?? s.LegalName).FirstOrDefaultAsync(cancellationToken) ?? "the company";
        var system = new[]
        {
            new ClaudeSystemBlock(InsightNarrativePrompt.Instructions(company)),
            new ClaudeSystemBlock(InsightNarrativePrompt.Metrics(computed.AsOf, metricsJson), CacheAfter: true),
        };
        var known = NarrativeNumberCheck.NumbersIn(metricsJson);
        var cost = 0m;

        try
        {
            var first = await claude.SendAsync(new ClaudeRequest(Feature, system, [new ClaudeText(InsightNarrativePrompt.Request)], InsightNarrativePrompt.Schema), cancellationToken);
            cost += first.Usage.CostUsd;
            var reply = Parse(first.Text);
            var unverified = Unverified(reply, known);
            if (unverified.Count > 0)
            {
                logger.LogInformation("Insights briefing quoted figures not in the metrics ({Figures}); asking again", string.Join(", ", unverified));
                var retry = await claude.SendAsync(new ClaudeRequest(Feature, system, [new ClaudeText(InsightNarrativePrompt.Retry(first.Text, unverified))], InsightNarrativePrompt.Schema), cancellationToken);
                cost += retry.Usage.CostUsd;
                reply = Parse(retry.Text);
            }

            return Keep(reply, known, computed, cost);
        }
        catch (Exception exception) when (exception is AiServiceException or AiRefusedException or AiBudgetExceededException or AiUnavailableException or JsonException)
        {
            logger.LogWarning(exception, "Insights briefing failed");
            var reason = exception is JsonException ? "Claude's reply couldn't be read." : exception.Message;
            return new Briefing(null, NarrativeStatus.Failed, $"The written summary couldn't be produced: {reason}", cost);
        }
    }

    private static Reply Parse(string json) =>
        JsonSerializer.Deserialize<Reply>(json, Json) is { Headline: not null, Summary: not null, Cards: not null } reply
            ? reply
            : throw new JsonException("The briefing was incomplete.");

    private static List<string> Unverified(Reply reply, List<decimal> known) =>
        [.. new[] { reply.Headline, reply.Summary }.Concat(reply.Cards.Select(c => $"{c.Title} {c.Body}"))
            .SelectMany(text => NarrativeNumberCheck.Unverified(text, known)).Distinct()];

    /// <summary>Keeps the parts whose figures all check out; a headline that doesn't is replaced with a computed one.</summary>
    private static Briefing Keep(Reply reply, List<decimal> known, InsightMetricsDto computed, decimal cost)
    {
        bool Checks(string text) => NarrativeNumberCheck.Unverified(text, known).Count == 0;

        var removed = Unverified(reply, known);
        var cards = reply.Cards
            .Take(InsightNarrativePrompt.MaxCards)
            .Where(c => Checks($"{c.Title} {c.Body}"))
            .Select(c => new InsightCardDto(Kind(c.Kind), c.Title.Trim(), c.Body.Trim()))
            .ToList();
        var summary = Checks(reply.Summary) ? reply.Summary.Trim() : "";
        var headline = Checks(reply.Headline) ? reply.Headline.Trim() : DefaultHeadline(computed);

        if (removed.Count == 0)
        {
            return new Briefing(new InsightNarrativeDto(headline, summary, cards), NarrativeStatus.Verified, null, cost);
        }

        var note = $"Left out because these figures aren't in the metrics: {string.Join(", ", removed)}.";
        return cards.Count > 0 || summary.Length > 0
            ? new Briefing(new InsightNarrativeDto(headline, summary, cards), NarrativeStatus.Trimmed, note, cost)
            : new Briefing(null, NarrativeStatus.Failed, note, cost);
    }

    private static InsightCardKind Kind(string kind) => kind.ToLowerInvariant() switch
    {
        "opportunity" => InsightCardKind.Opportunity,
        "risk" => InsightCardKind.Risk,
        "action" => InsightCardKind.Action,
        _ => InsightCardKind.Info,
    };

    /// <summary>A headline built from the metrics themselves, used when Claude's doesn't check out.</summary>
    internal static string DefaultHeadline(InsightMetricsDto computed)
    {
        var sales = computed.Sales;
        var amount = "₹" + sales.FinancialYearToDate.ToString("#,##0", India);
        return sales.GrowthPercent is { } growth
            ? $"Sales this financial year: {amount}, {(growth >= 0 ? "up" : "down")} {Math.Abs(growth).ToString("0.0", CultureInfo.InvariantCulture)}% on last year"
            : $"Sales this financial year: {amount}";
    }

    private InsightsDto ToDto(InsightRun run) => new(
        run.Id,
        run.CreatedAtUtc,
        run.Trigger,
        JsonSerializer.Deserialize<InsightMetricsDto>(run.MetricsJson, Json)!,
        run.NarrativeJson is null ? null : JsonSerializer.Deserialize<InsightNarrativeDto>(run.NarrativeJson, Json),
        run.NarrativeStatus,
        run.NarrativeNote,
        Math.Round(run.CostUsd * aiOptions.CurrentValue.UsdToInr, 2));
}
