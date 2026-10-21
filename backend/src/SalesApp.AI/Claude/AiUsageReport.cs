using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Claude;

public interface IAiUsageReport
{
    Task<AiUsageSummaryDto> SummaryAsync(int days, CancellationToken cancellationToken);
    Task<PagedResult<AiCallDto>> CallsAsync(AiCallListRequest request, CancellationToken cancellationToken);
    Task<PagedResult<AiQueryDto>> QueriesAsync(AiQueryListRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The AI usage log and the AI query log, summarised for administrators: spend per day, feature and user (with
/// each user's last 24 hours against the daily budget), how much input came from the prompt cache, and every call
/// and query with its outcome.
/// </summary>
public sealed class AiUsageReport(AppDbContext db, IBusinessClock clock, TimeProvider time, IOptionsMonitor<AiOptions> options) : IAiUsageReport
{
    private static string Background => L.Show("Background (morning insights)");

    public async Task<AiUsageSummaryDto> SummaryAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 365);
        var ai = options.CurrentValue;
        var now = time.GetUtcNow().UtcDateTime;
        var firstDay = clock.Today.AddDays(1 - days);
        var since = TimeZoneInfo.ConvertTimeToUtc(firstDay.ToDateTime(TimeOnly.MinValue), clock.TimeZone);

        var logs = await db.AiUsageLogs.AsNoTracking()
            .Where(l => l.CreatedAtUtc >= since)
            .Select(l => new { l.CreatedAtUtc, l.Feature, l.UserId, UserName = l.User != null ? l.User.FullName : null, l.Outcome, l.CostUsd, l.DurationMs, l.InputTokens, l.CacheReadTokens, l.CacheWriteTokens })
            .ToListAsync(cancellationToken);

        decimal Inr(decimal usd) => Math.Round(usd * ai.UsdToInr, 2);
        var byDay = logs.GroupBy(l => clock.ToBusinessDate(l.CreatedAtUtc)).ToDictionary(g => g.Key, g => (Cost: g.Sum(l => l.CostUsd), Calls: g.Count()));
        var daily = Enumerable.Range(0, days)
            .Select(i => firstDay.AddDays(i))
            .Select(day => new AiDailyCostDto(day, Inr(byDay.GetValueOrDefault(day).Cost), byDay.GetValueOrDefault(day).Calls))
            .ToList();

        var byFeature = logs.GroupBy(l => l.Feature)
            .Select(g => new AiFeatureCostDto(g.Key, g.Count(), Inr(g.Sum(l => l.CostUsd)), Inr(g.Average(l => l.CostUsd)),
                (int)g.Average(l => l.DurationMs), g.Count(l => l.Outcome != AiOutcome.Success)))
            .OrderByDescending(f => f.CostInr)
            .ToList();

        var dayAgo = now.AddDays(-1);
        var budget = ai.DailyBudgetUsdPerUser;
        var byUser = logs.GroupBy(l => l.UserId)
            .Select(g =>
            {
                var last24 = g.Where(l => l.CreatedAtUtc >= dayAgo).Sum(l => l.CostUsd);
                return new AiUserCostDto(g.Key, g.Key is null ? Background : g.First().UserName ?? L.Show("(deleted user)"), g.Count(), Inr(g.Sum(l => l.CostUsd)),
                    Inr(last24), budget > 0 ? Math.Round(last24 / budget * 100, 1) : 0);
            })
            .OrderByDescending(u => u.CostInr)
            .ToList();

        // Of all the input Claude read, the share that came from the prompt cache (billed at a twentieth of the price).
        var input = logs.Sum(l => (long)l.InputTokens + l.CacheReadTokens + l.CacheWriteTokens);
        var cached = logs.Sum(l => (long)l.CacheReadTokens);

        return new AiUsageSummaryDto(
            days,
            Math.Round(logs.Sum(l => l.CostUsd), 4),
            Inr(logs.Sum(l => l.CostUsd)),
            Inr(byDay.GetValueOrDefault(clock.Today).Cost),
            logs.Count,
            logs.Count(l => l.Outcome == AiOutcome.Failed),
            logs.Count(l => l.Outcome == AiOutcome.Refused),
            logs.Count(l => l.Outcome == AiOutcome.Truncated),
            input > 0 ? Math.Round(cached * 100m / input, 1) : 0,
            budget,
            daily,
            byFeature,
            byUser);
    }

    public async Task<PagedResult<AiCallDto>> CallsAsync(AiCallListRequest request, CancellationToken cancellationToken)
    {
        var rate = options.CurrentValue.UsdToInr;
        var query = db.AiUsageLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Feature))
        {
            query = query.Where(l => l.Feature == request.Feature);
        }

        if (request.ProblemsOnly)
        {
            query = query.Where(l => l.Outcome != AiOutcome.Success);
        }

        return await query
            .OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id)
            .Select(l => new AiCallDto(l.Id, l.CreatedAtUtc, l.Feature, l.Model, l.UserId == null ? Background : l.User != null ? l.User.FullName : L.Show("(deleted user)"),
                l.InputTokens, l.OutputTokens, l.CacheReadTokens, l.CacheWriteTokens, Math.Round(l.CostUsd * rate, 2), l.DurationMs, l.Outcome, l.Error))
            .ToPageAsync(request, cancellationToken);
    }

    public async Task<PagedResult<AiQueryDto>> QueriesAsync(AiQueryListRequest request, CancellationToken cancellationToken)
    {
        var query = db.AiQueryLogs.AsNoTracking();
        if (Enum.TryParse<AiQueryOutcome>(request.Outcome, ignoreCase: true, out var outcome))
        {
            query = query.Where(l => l.Outcome == outcome);
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim();
            query = query.Where(l => l.Question.Contains(q) || l.Sql.Contains(q));
        }

        return await query
            .OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id)
            .Select(l => new AiQueryDto(l.Id, l.CreatedAtUtc, l.User != null ? l.User.FullName : L.Show("(deleted user)"), l.Question, l.Sql, l.RunAs, l.Outcome, l.Error, l.RowCount, l.DurationMs))
            .ToPageAsync(request, cancellationToken);
    }
}

internal static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPageAsync<T>(this IQueryable<T> query, PageRequest request, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, request.Page, request.PageSize, total);
    }
}
