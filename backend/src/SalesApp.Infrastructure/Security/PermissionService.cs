using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Security;

/// <summary>A signed-in user's role name and what they may do.</summary>
public sealed record UserAccess(string Role, IReadOnlySet<string> Permissions);

public interface IPermissionService
{
    /// <summary>The user's role and permissions, or null when the account doesn't exist or is deactivated.</summary>
    Task<UserAccess?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Call after changing a role, a user's role or a user's permissions: the next request sees the change.</summary>
    void Invalidate();
}

/// <summary>
/// Bumped whenever roles or permissions change. Cached access entries carry the version they were read at, so a
/// change makes every cached entry stale at once without tracking which users it touched.
/// </summary>
public sealed class PermissionVersion
{
    private int _current;

    public int Current => Volatile.Read(ref _current);

    public void Bump() => Interlocked.Increment(ref _current);
}

/// <summary>
/// Reads a user's access from the database, cached for a few minutes because every API request needs it. The
/// Administrator role holds every permission; other roles hold what is stored for them, adjusted by the user's own
/// changes (see <see cref="Permissions.Effective"/>).
/// </summary>
public sealed class PermissionService(AppDbContext db, IMemoryCache cache, PermissionVersion version) : IPermissionService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<UserAccess?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = (nameof(PermissionService), userId, version.Current);
        if (cache.TryGetValue(key, out UserAccess? access))
        {
            return access;
        }

        access = await LoadAsync(userId, cancellationToken);
        cache.Set(key, access, CacheFor);
        return access;
    }

    public void Invalidate() => version.Bump();

    private async Task<UserAccess?> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (!active)
        {
            return null;
        }

        var role = await (from ur in db.UserRoles.AsNoTracking()
                          join r in db.Roles on ur.RoleId equals r.Id
                          where ur.UserId == userId
                          select new { r.Id, r.Name, r.IsSystem })
            .FirstOrDefaultAsync(cancellationToken);
        if (role is null)
        {
            return new UserAccess(string.Empty, new HashSet<string>());
        }

        if (role.IsSystem)
        {
            return new UserAccess(role.Name ?? string.Empty, Permissions.All);
        }

        var rolePermissions = await db.RoleGrants.AsNoTracking()
            .Where(p => p.RoleId == role.Id)
            .Select(p => p.Permission)
            .ToListAsync(cancellationToken);
        var changes = await db.UserPermissionChanges.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        return new UserAccess(
            role.Name ?? string.Empty,
            Permissions.Effective(rolePermissions, changes.Where(c => c.IsGranted).Select(c => c.Permission), changes.Where(c => !c.IsGranted).Select(c => c.Permission)));
    }
}
