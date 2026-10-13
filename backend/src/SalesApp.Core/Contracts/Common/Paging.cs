using System.ComponentModel.DataAnnotations;

namespace SalesApp.Core.Contracts.Common;

/// <summary>Query-string parameters shared by every paged list endpoint.</summary>
public record PageRequest
{
    /// <summary>Free-text search; each list decides which columns it matches.</summary>
    [MaxLength(100)]
    public string? Q { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    /// <summary>Column to sort by; prefix with '-' for descending, e.g. "-createdAt".</summary>
    [MaxLength(40)]
    public string? Sort { get; init; }

    public bool IncludeInactive { get; init; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
