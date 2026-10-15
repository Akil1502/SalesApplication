using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data;

/// <summary>Stamps CreatedAtUtc/UpdatedAtUtc on <see cref="IAuditable"/> entities, so no service has to remember to.</summary>
public sealed class AuditTimestampsInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
            {
                if (entry.State == EntityState.Added)
                {
                    // Respect timestamps set explicitly (the demo-data generator writes history).
                    if (entry.Entity.CreatedAtUtc == default)
                    {
                        entry.Entity.CreatedAtUtc = now;
                    }

                    if (entry.Entity.UpdatedAtUtc == default)
                    {
                        entry.Entity.UpdatedAtUtc = entry.Entity.CreatedAtUtc;
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Property(e => e.CreatedAtUtc).IsModified = false;
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
