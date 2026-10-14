using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;

namespace SalesApp.Core.Contracts.Ai;

/// <summary>
/// What the AI features have cost over a period, for administrators. Costs are estimates from the token counts
/// and the configured price list; Anthropic's console has the actual bill.
/// </summary>
public sealed record AiUsageSummaryDto(
    int Days,
    decimal CostUsd,
    decimal CostInr,
    decimal TodayCostInr,
    int Calls,
    int Failed,
    int Refused,
    int Truncated,
    decimal CacheReadPercent,
    decimal DailyBudgetUsdPerUser,
    IReadOnlyList<AiDailyCostDto> Daily,
    IReadOnlyList<AiFeatureCostDto> ByFeature,
    IReadOnlyList<AiUserCostDto> ByUser);

/// <summary>Cost per day (India time).</summary>
public sealed record AiDailyCostDto(DateOnly Day, decimal CostInr, int Calls);

public sealed record AiFeatureCostDto(string Feature, int Calls, decimal CostInr, decimal AverageCostInr, int AverageDurationMs, int Failed);

/// <summary>
/// Spend per user; <see cref="UserId"/> is null for background work (the morning insights).
/// <see cref="BudgetUsedPercent"/> is the last 24 hours against the per-user daily budget.
/// </summary>
public sealed record AiUserCostDto(Guid? UserId, string Name, int Calls, decimal CostInr, decimal Last24HoursInr, decimal BudgetUsedPercent);

public sealed record AiCallDto(
    long Id,
    DateTime CreatedAtUtc,
    string Feature,
    string Model,
    string UserName,
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheWriteTokens,
    decimal CostInr,
    int DurationMs,
    AiOutcome Outcome,
    string? Error);

public sealed record AiQueryDto(
    long Id,
    DateTime CreatedAtUtc,
    string UserName,
    string Question,
    string Sql,
    string RunAs,
    AiQueryOutcome Outcome,
    string? Error,
    int RowCount,
    int DurationMs);

public sealed record AiCallListRequest : PageRequest
{
    public string? Feature { get; init; }

    /// <summary>Only calls that didn't succeed (failed, refused or cut off).</summary>
    public bool ProblemsOnly { get; init; }
}

public sealed record AiQueryListRequest : PageRequest
{
    /// <summary>Succeeded, Rejected or Failed; omitted for all. (A string, so the outcome enum stays non-nullable in the API schema.)</summary>
    public string? Outcome { get; init; }
}
