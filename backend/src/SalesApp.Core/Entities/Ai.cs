namespace SalesApp.Core.Entities;

public enum AiOutcome
{
    Success,

    /// <summary>Claude declined the request (stop reason "refusal"). Billed like any other call.</summary>
    Refused,

    /// <summary>The reply hit the output-token limit and was cut off.</summary>
    Truncated,

    /// <summary>The call failed: network, rate limit, invalid key, or an unreadable reply.</summary>
    Failed,
}

/// <summary>
/// One Claude API call: who made it, for which feature, the tokens it used and what it cost. Written for every
/// call, successful or not, so usage and spend can be audited and the per-user daily budget enforced.
/// </summary>
public sealed class AiUsageLog
{
    public long Id { get; set; }

    /// <summary>The app feature that made the call, such as "OrderEntry".</summary>
    public required string Feature { get; set; }
    public required string Model { get; set; }

    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int DurationMs { get; set; }

    /// <summary>Input tokens billed at the full rate (the part of the prompt that wasn't cached).</summary>
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }

    /// <summary>Estimated from the configured price list; the Anthropic console has the authoritative bill.</summary>
    public decimal CostUsd { get; set; }

    public AiOutcome Outcome { get; set; }
    public string? Error { get; set; }

    /// <summary>Anthropic's message id, for matching a call against their console or support.</summary>
    public string? RequestId { get; set; }
}

/// <summary>
/// Another name customers use for a product ("30s white", "carded 30"), learned when someone corrects smart order
/// entry. Aliases go into the catalog Claude matches against, so the same wording is recognised next time.
/// </summary>
public sealed class ProductAlias
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>The wording as it appeared in the order.</summary>
    public required string Alias { get; set; }

    /// <summary>Lower case with punctuation and extra spaces removed; unique, so one wording maps to one product.</summary>
    public required string NormalizedAlias { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public enum AiQueryOutcome
{
    Succeeded,

    /// <summary>The SQL checker refused the query before it reached the database.</summary>
    Rejected,

    /// <summary>The database refused or failed the query (permission, syntax, timeout).</summary>
    Failed,
}

/// <summary>
/// One SQL query the AI tried while answering a question: what it ran, whether the checker or the database refused
/// it, and how many rows came back. Kept for auditing the "ask your data" feature.
/// </summary>
public sealed class AiQueryLog
{
    public long Id { get; set; }

    /// <summary>Groups the queries made while answering one question.</summary>
    public Guid RunId { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public required string Question { get; set; }
    public required string Sql { get; set; }

    /// <summary>The database user the query ran as: ai_reader, or ai_reader_mgr for managers.</summary>
    public required string RunAs { get; set; }

    public AiQueryOutcome Outcome { get; set; }
    public string? Error { get; set; }
    public int RowCount { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
