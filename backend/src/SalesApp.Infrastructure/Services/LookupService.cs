using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ILookupService
{
    /// <summary>All states and union territories, keyed by GST state code. Cached: it's migration-seeded reference data.</summary>
    Task<IReadOnlyDictionary<string, StateDto>> GetStatesAsync(CancellationToken cancellationToken);

    /// <summary>Active users who can be assigned customers (sales reps and managers).</summary>
    Task<IReadOnlyList<UserLookupDto>> GetSalesRepsAsync(CancellationToken cancellationToken);

    Task<StateDto> GetStateOrThrowAsync(string? code, string field, CancellationToken cancellationToken);
}

public sealed class LookupService(AppDbContext db, IMemoryCache cache) : ILookupService
{
    private const string StatesCacheKey = "lookups:states";

    public async Task<IReadOnlyDictionary<string, StateDto>> GetStatesAsync(CancellationToken cancellationToken)
    {
        var states = await cache.GetOrCreateAsync(StatesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            var rows = await db.States.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
            return (IReadOnlyDictionary<string, StateDto>)rows.ToDictionary(
                s => s.Code, s => new StateDto(s.Code, s.Name, s.UsesUtgst), StringComparer.Ordinal);
        });
        return states!;
    }

    public async Task<StateDto> GetStateOrThrowAsync(string? code, string field, CancellationToken cancellationToken)
    {
        var states = await GetStatesAsync(cancellationToken);
        return code is not null && states.TryGetValue(code, out var state)
            ? state
            : throw new Core.Common.ValidationException(field, "Choose a valid state.");
    }

    public async Task<IReadOnlyList<UserLookupDto>> GetSalesRepsAsync(CancellationToken cancellationToken)
    {
        var roleIds = db.Roles
            .Where(r => r.Name == Roles.SalesRep || r.Name == Roles.SalesManager)
            .Select(r => r.Id);

        return await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)))
            .OrderBy(u => u.FullName)
            .Select(u => new UserLookupDto(u.Id, u.FullName, u.Email ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    /// <summary>True when the user exists, is active, and holds a role that can own customers.</summary>
    internal static async Task<bool> IsAssignableRepAsync(UserManager<AppUser> users, Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is not { IsActive: true })
        {
            return false;
        }

        var roles = await users.GetRolesAsync(user);
        return roles.Contains(Roles.SalesRep) || roles.Contains(Roles.SalesManager);
    }
}
