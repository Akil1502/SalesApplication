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
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.UpdatedAtUtc = now;
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
