using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;

namespace SalesApp.Infrastructure.Services;

internal static class QueryHelpers
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageRequest request, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, request.Page, request.PageSize, total);
    }

    /// <summary>"name" -> ("name", false); "-createdAt" -> ("createdAt", true).</summary>
    public static (string Key, bool Descending) ParseSort(string? sort, string defaultSort)
    {
        var value = string.IsNullOrWhiteSpace(sort) ? defaultSort : sort.Trim();
        return value.StartsWith('-') ? (value[1..], true) : (value, false);
    }
}

/// <summary>
/// Whitelist of sortable columns for one list endpoint. Unknown sort keys are a validation error
/// rather than silently ignored, and every sort ends with a unique tiebreaker so paging is stable.
/// </summary>
internal sealed class SortMap<T>
{
    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _sorts =
        new(StringComparer.OrdinalIgnoreCase);

    public SortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> key)
    {
        _sorts[name] = (query, descending) => descending ? query.OrderByDescending(key) : query.OrderBy(key);
        return this;
    }

    public IOrderedQueryable<T> Apply(IQueryable<T> query, string? sort, string defaultSort)
    {
        var (key, descending) = QueryHelpers.ParseSort(sort, defaultSort);
        if (!_sorts.TryGetValue(key, out var apply))
        {
            throw new ValidationException("sort", $"Can't sort by '{key}'. Use one of: {string.Join(", ", _sorts.Keys)}.");
        }

        return apply(query, descending);
    }
}

internal static class RowVersions
{
    public static string ToToken(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    /// <summary>Decodes the client's copy of the row version; a missing or garbled value is a validation error.</summary>
    public static byte[] Parse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ValidationException("rowVersion", "rowVersion is required when updating. Reload the record and try again.");
        }

        try
        {
            return Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw new ValidationException("rowVersion", "rowVersion is not valid. Reload the record and try again.");
        }
    }
}

internal static class Text
{
    /// <summary>Trims, and turns empty strings into null so optional columns stay NULL rather than ''.</summary>
    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Removes spaces and hyphens from phone numbers so they're stored consistently.</summary>
    public static string? NormalizePhone(string? value) =>
        NullIfBlank(value)?.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
}
