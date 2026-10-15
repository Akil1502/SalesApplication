namespace SalesApp.Core.Entities;

public enum InsightTrigger
{
    Nightly,
    Manual,
}

/// <summary>Whether the written summary could be produced and how its figures checked out.</summary>
public enum NarrativeStatus
{
    /// <summary>AI is off or has no key: the figures are shown without a written summary.</summary>
    AiOff,

    /// <summary>Every figure in the summary was found in the computed metrics.</summary>
    Verified,

    /// <summary>Cards with figures that couldn't be found were removed; the rest checked out.</summary>
    Trimmed,

    /// <summary>The summary couldn't be written or didn't check out; only the figures are shown.</summary>
    Failed,
}

/// <summary>
/// One computation of the insights: the metrics as JSON (exactly what Claude was given), the written summary that
/// survived the figure check, and what it cost. Runs nightly and when a manager refreshes; the latest is shown.
/// </summary>
public sealed class InsightRun
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public InsightTrigger Trigger { get; set; }

    /// <summary>The manager who pressed Refresh; null for the nightly run.</summary>
    public Guid? RequestedByUserId { get; set; }
    public AppUser? RequestedBy { get; set; }

    public required string MetricsJson { get; set; }
    public string? NarrativeJson { get; set; }
    public NarrativeStatus NarrativeStatus { get; set; }

    /// <summary>Why the summary is missing or shortened (for example, which figures couldn't be found).</summary>
    public string? NarrativeNote { get; set; }

    public decimal CostUsd { get; set; }
    public int DurationMs { get; set; }
}
