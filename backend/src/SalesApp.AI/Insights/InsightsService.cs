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
using SalesApp.Infrastructure.Notifications;
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
    INotificationWriter notifications,
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
            : new Briefing(null, null, NarrativeStatus.AiOff, L.Msg("AI is switched off or has no API key, so there's no written summary."), 0m);

        var run = new InsightRun
        {
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            Trigger = trigger,
            RequestedByUserId = currentUser.IsSystem ? null : currentUser.UserId,
            MetricsJson = metricsJson,
            NarrativeJson = briefing.Narrative is null ? null : JsonSerializer.Serialize(briefing.Narrative, Json),
            NarrativeTamilJson = briefing.Tamil is null ? null : JsonSerializer.Serialize(briefing.Tamil, Json),
            NarrativeStatus = briefing.Status,
            NarrativeNote = briefing.Note is { Length: > 1000 } note ? note[..1000] : briefing.Note,
            CostUsd = briefing.CostUsd,
            DurationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        };
        db.InsightRuns.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        // Fixed words rather than Claude's headline, so everyone reads it in their own language.
        await notifications.RaiseAsync(new NotificationEvent(
            NotificationKind.InsightsReady,
            L.Msg("New insights are ready"),
            L.Msg("Forecasts, unusual months and reorder suggestions have been worked out again."),
            "/insights"), CancellationToken.None);
        return ToDto(run);
    }

    private sealed record Briefing(InsightNarrativeDto? Narrative, InsightNarrativeDto? Tamil, NarrativeStatus Status, string? Note, decimal CostUsd);

    private sealed record Reply(string Headline, string Summary, List<ReplyCard> Cards, TamilReply? Tamil);

    private sealed record ReplyCard(string Kind, string Title, string Body);

    private sealed record TamilReply(string Headline, string Summary, List<TamilCard> Cards);

    private sealed record TamilCard(string Title, string Body);

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
            var reason = exception is JsonException ? L.Msg("Claude's reply couldn't be read.") : exception.Message;
            return new Briefing(null, null, NarrativeStatus.Failed, L.Msg($"The written summary couldn't be produced: {reason}"), cost);
        }
    }

    private static Reply Parse(string json) =>
        JsonSerializer.Deserialize<Reply>(json, Json) is { Headline: not null, Summary: not null, Cards: not null } reply
            ? reply
            : throw new JsonException("The briefing was incomplete.");

    private static List<string> Unverified(Reply reply, List<decimal> known) =>
        [.. new[] { reply.Headline, reply.Summary }.Concat(reply.Cards.Select(c => $"{c.Title} {c.Body}"))
            .Concat(reply.Tamil is { } tamil ? new[] { tamil.Headline, tamil.Summary }.Concat(tamil.Cards.Select(c => $"{c.Title} {c.Body}")) : [])
            .SelectMany(text => NarrativeNumberCheck.Unverified(text, known)).Distinct()];

    /// <summary>
    /// Keeps the parts whose figures all check out; a headline that doesn't is replaced with a computed one. A Tamil
    /// card is kept only beside its English card, so the two versions say the same things.
    /// </summary>
    private static Briefing Keep(Reply reply, List<decimal> known, InsightMetricsDto computed, decimal cost)
    {
        bool Checks(string text) => NarrativeNumberCheck.Unverified(text, known).Count == 0;

        var removed = Unverified(reply, known);
        var english = new List<InsightCardDto>();
        var tamil = new List<InsightCardDto>();
        var tamilCards = reply.Tamil?.Cards ?? [];
        foreach (var (card, i) in reply.Cards.Take(InsightNarrativePrompt.MaxCards).Select((c, i) => (c, i)))
        {
            if (!Checks($"{card.Title} {card.Body}"))
            {
                continue;
            }

            english.Add(new InsightCardDto(Kind(card.Kind), card.Title.Trim(), card.Body.Trim()));
            if (i < tamilCards.Count && Checks($"{tamilCards[i].Title} {tamilCards[i].Body}"))
            {
                tamil.Add(new InsightCardDto(Kind(card.Kind), tamilCards[i].Title.Trim(), tamilCards[i].Body.Trim()));
            }
        }

        var summary = Checks(reply.Summary) ? reply.Summary.Trim() : "";
        var headline = Checks(reply.Headline) ? reply.Headline.Trim() : DefaultHeadline(computed);
        InsightNarrativeDto? inTamil = null;
        if (reply.Tamil is { } t)
        {
            var tamilHeadline = Checks(t.Headline) && headline == reply.Headline.Trim() ? t.Headline.Trim() : L.ToTamil(headline);
            inTamil = new InsightNarrativeDto(tamilHeadline, summary.Length > 0 && Checks(t.Summary) ? t.Summary.Trim() : "", tamil);
        }

        if (removed.Count == 0)
        {
            return new Briefing(new InsightNarrativeDto(headline, summary, english), inTamil, NarrativeStatus.Verified, null, cost);
        }

        var note = L.Msg($"Left out because these figures aren't in the metrics: {string.Join(", ", removed)}.");
        return english.Count > 0 || summary.Length > 0
            ? new Briefing(new InsightNarrativeDto(headline, summary, english), inTamil, NarrativeStatus.Trimmed, note, cost)
            : new Briefing(null, null, NarrativeStatus.Failed, note, cost);
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
        if (sales.GrowthPercent is not { } growth)
        {
            return L.Msg($"Sales this financial year: {amount}");
        }

        var change = Math.Abs(growth).ToString("0.0", CultureInfo.InvariantCulture);
        return growth >= 0
            ? L.Msg($"Sales this financial year: {amount}, up {change}% on last year")
            : L.Msg($"Sales this financial year: {amount}, down {change}% on last year");
    }

    /// <summary>In the reader's language: the Tamil write-up when there is one, and the computed alerts and notes translated.</summary>
    private InsightsDto ToDto(InsightRun run)
    {
        var metrics = JsonSerializer.Deserialize<InsightMetricsDto>(run.MetricsJson, Json)!;
        var narrative = L.InTamil && run.NarrativeTamilJson is { } tamil ? tamil : run.NarrativeJson;
        return new(
            run.Id,
            run.CreatedAtUtc,
            run.Trigger,
            metrics with { Anomalies = [.. metrics.Anomalies.Select(a => a with { Title = L.T(a.Title), Detail = L.T(a.Detail) })] },
            narrative is null ? null : JsonSerializer.Deserialize<InsightNarrativeDto>(narrative, Json),
            run.NarrativeStatus,
            L.T(run.NarrativeNote),
            Math.Round(run.CostUsd * aiOptions.CurrentValue.UsdToInr, 2));
    }
}
