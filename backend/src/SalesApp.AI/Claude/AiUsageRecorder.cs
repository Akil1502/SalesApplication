using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Claude;

public interface IAiUsageRecorder
{
    Task RecordAsync(AiUsageLog entry, CancellationToken cancellationToken);

    /// <summary>Estimated spend by this user (null: background work) over the last 24 hours, in US dollars.</summary>
    Task<decimal> SpentInLastDayAsync(Guid? userId, CancellationToken cancellationToken);
}

/// <summary>
/// Writes usage rows through its own DbContext, so a log entry is saved even when the feature's own work is later
/// rolled back, and never commits anything the feature has staged.
/// </summary>
public sealed class AiUsageRecorder(IServiceScopeFactory scopes, TimeProvider time) : IAiUsageRecorder
{
    public async Task RecordAsync(AiUsageLog entry, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AiUsageLogs.Add(entry);
        // Not the request's token: a call the user cancelled was still made and billed, so it's still logged.
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task<decimal> SpentInLastDayAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().UtcDateTime.AddDays(-1);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AiUsageLogs
            .Where(l => l.UserId == userId && l.CreatedAtUtc >= since)
            .SumAsync(l => (decimal?)l.CostUsd, cancellationToken) ?? 0m;
    }
}
