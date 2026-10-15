using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Security;

namespace SalesApp.Infrastructure.Services;

public interface IUserAdminService
{
    Task<PagedResult<UserDto>> ListAsync(UserListRequest request, CancellationToken cancellationToken);
    Task<UserDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UserDto> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken);
    Task<UserDto> UpdateAsync(Guid id, UserUpdateRequest request, CancellationToken cancellationToken);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// User management. Each user holds exactly one role, and may have permissions added to or taken away from that
/// role for them alone. Guard rails stop an admin from locking everyone out: you can't change your own role or
/// permissions or deactivate yourself, and the last active administrator stays.
/// </summary>
public sealed class UserAdminService(
    AppDbContext db,
    UserManager<AppUser> users,
    ICurrentUser currentUser,
    IPermissionService permissions,
    TimeProvider timeProvider) : IUserAdminService
{
    public async Task<PagedResult<UserDto>> ListAsync(UserListRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var query =
            from u in db.Users.AsNoTracking()
            let role = (from ur in db.UserRoles
                        join r in db.Roles on ur.RoleId equals r.Id
                        where ur.UserId == u.Id
                        select r.Name).FirstOrDefault()
            select new { User = u, Role = role ?? string.Empty };

        if (!request.IncludeInactive)
        {
            query = query.Where(x => x.User.IsActive);
        }

        if (Text.NullIfBlank(request.Role) is { } roleFilter)
        {
            query = query.Where(x => x.Role == roleFilter);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(x => x.User.FullName.Contains(q) || x.User.Email!.Contains(q));
        }

        var (sortKey, descending) = QueryHelpers.ParseSort(request.Sort, "fullName");
        var ordered = sortKey.ToUpperInvariant() switch
        {
            "FULLNAME" => descending ? query.OrderByDescending(x => x.User.FullName) : query.OrderBy(x => x.User.FullName),
            "EMAIL" => descending ? query.OrderByDescending(x => x.User.Email) : query.OrderBy(x => x.User.Email),
            "ROLE" => descending ? query.OrderByDescending(x => x.Role) : query.OrderBy(x => x.Role),
            "CREATEDAT" => descending ? query.OrderByDescending(x => x.User.CreatedAtUtc) : query.OrderBy(x => x.User.CreatedAtUtc),
            _ => throw new ValidationException("sort", $"Can't sort by '{sortKey}'. Use one of: fullName, email, role, createdAt."),
        };

        var page = await ordered
            .ThenBy(x => x.User.Id)
            .Select(x => new UserDto(
                x.User.Id, x.User.Email ?? string.Empty, x.User.FullName, x.Role, x.User.IsActive,
                x.User.LockoutEnd != null && x.User.LockoutEnd > now,
                x.User.CreatedAtUtc, new List<string>(), new List<string>()))
            .ToPagedResultAsync(request, cancellationToken);

        var ids = page.Items.Select(u => u.Id).ToList();
        var changes = await db.UserPermissionChanges.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToListAsync(cancellationToken);
        return page with
        {
            Items = [.. page.Items.Select(u => u with
            {
                AddedPermissions = Keys(changes.Where(c => c.UserId == u.Id && c.IsGranted)),
                RemovedPermissions = Keys(changes.Where(c => c.UserId == u.Id && !c.IsGranted)),
            })],
        };
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id);
        return await ToDtoAsync(user);
    }

    public async Task<UserDto> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken)
    {
        var role = await FindRoleAsync(request.Role, cancellationToken);

        var email = request.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
        {
            throw new ValidationException("email", "A user with this email already exists.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };

        // Create and role assignment succeed or fail together.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(await users.CreateAsync(user, request.Password), "password");
        ThrowIfFailed(await users.AddToRoleAsync(user, role.Name!), "role");
        await SetChangesAsync(user.Id, role, request.AddedPermissions, request.RemovedPermissions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        permissions.Invalidate();
        return await ToDtoAsync(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UserUpdateRequest request, CancellationToken cancellationToken)
    {
        var role = await FindRoleAsync(request.Role, cancellationToken);
        var user = await FindAsync(id);
        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault();

        var isSelf = id == currentUser.UserId;
        if (isSelf && role.Name != currentRole)
        {
            throw new ValidationException("role", "You can't change your own role. Ask another admin.");
        }

        if (isSelf && !await SameChangesAsync(id, role, request.AddedPermissions, request.RemovedPermissions, cancellationToken))
        {
            throw new ValidationException("permissions", "You can't change your own permissions. Ask another admin.");
        }

        if (isSelf && !request.IsActive)
        {
            throw new ValidationException("isActive", "You can't deactivate your own account.");
        }

        var losingAdmin = currentRole == Roles.Admin && user.IsActive && (role.Name != Roles.Admin || !request.IsActive);
        if (losingAdmin && await CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            throw new ConflictException("This is the only active admin. Make someone else an admin first.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var wasActive = user.IsActive;
        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;
        ThrowIfFailed(await users.UpdateAsync(user), "fullName");

        if (role.Name != currentRole)
        {
            if (currentRole is not null)
            {
                ThrowIfFailed(await users.RemoveFromRoleAsync(user, currentRole), "role");
            }

            ThrowIfFailed(await users.AddToRoleAsync(user, role.Name!), "role");
        }

        await SetChangesAsync(user.Id, role, request.AddedPermissions, request.RemovedPermissions, cancellationToken);

        // Rotating the security stamp marks the change; deactivated users are also refused by /me and login.
        if (wasActive && !request.IsActive)
        {
            await users.UpdateSecurityStampAsync(user);
        }

        await transaction.CommitAsync(cancellationToken);
        permissions.Invalidate();
        return await ToDtoAsync(user);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(id);

        // Validate first so a weak password never leaves the account without one.
        foreach (var validator in users.PasswordValidators)
        {
            ThrowIfFailed(await validator.ValidateAsync(users, user, request.NewPassword), "newPassword");
        }

        // An admin reset is a direct replacement (no emailed token), done atomically.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(await users.RemovePasswordAsync(user), "newPassword");
        ThrowIfFailed(await users.AddPasswordAsync(user, request.NewPassword), "newPassword");

        // A reset is how an admin helps a locked-out user, so clear the lockout too.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<AppUser> FindAsync(Guid id) =>
        await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException($"User {id} was not found.");

    private async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        await (from u in db.Users
               join ur in db.UserRoles on u.Id equals ur.UserId
               join r in db.Roles on ur.RoleId equals r.Id
               where r.Name == Roles.Admin && u.IsActive
               select u.Id).CountAsync(cancellationToken);

    private async Task<UserDto> ToDtoAsync(AppUser user)
    {
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        var lockedOut = await users.IsLockedOutAsync(user);
        var changes = await db.UserPermissionChanges.AsNoTracking().Where(p => p.UserId == user.Id).ToListAsync();
        return new UserDto(user.Id, user.Email ?? string.Empty, user.FullName, role, user.IsActive, lockedOut, user.CreatedAtUtc,
            Keys(changes.Where(c => c.IsGranted)), Keys(changes.Where(c => !c.IsGranted)));
    }

    private async Task<AppRole> FindRoleAsync(string name, CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Name == name, cancellationToken)
        ?? throw new ValidationException("role", $"There is no role called {name}.");

    /// <summary>
    /// The user's own changes, kept only where they differ from the role: added permissions the role lacks (with what
    /// they need), removed permissions the role has. Administrators have everything, so they get no changes.
    /// </summary>
    private static (HashSet<string> Added, HashSet<string> Removed) Normalize(AppRole role, IEnumerable<string> added, IEnumerable<string> removed)
    {
        if (role.IsSystem)
        {
            return ([], []);
        }

        var rolePermissions = role.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        var addedSet = Permissions.WithNeeds(added);
        addedSet.ExceptWith(rolePermissions);
        var removedSet = removed.Where(rolePermissions.Contains).ToHashSet(StringComparer.Ordinal);
        return (addedSet, removedSet);
    }

    private async Task SetChangesAsync(Guid userId, AppRole role, IEnumerable<string> added, IEnumerable<string> removed, CancellationToken cancellationToken)
    {
        var (addedSet, removedSet) = Normalize(role, added, removed);
        await db.UserPermissionChanges.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        db.UserPermissionChanges.AddRange(addedSet.Select(p => new UserPermissionChange { UserId = userId, Permission = p, IsGranted = true }));
        db.UserPermissionChanges.AddRange(removedSet.Select(p => new UserPermissionChange { UserId = userId, Permission = p, IsGranted = false }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> SameChangesAsync(Guid userId, AppRole role, IEnumerable<string> added, IEnumerable<string> removed, CancellationToken cancellationToken)
    {
        var (addedSet, removedSet) = Normalize(role, added, removed);
        var stored = await db.UserPermissionChanges.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        return addedSet.SetEquals(stored.Where(c => c.IsGranted).Select(c => c.Permission))
            && removedSet.SetEquals(stored.Where(c => !c.IsGranted).Select(c => c.Permission));
    }

    private static List<string> Keys(IEnumerable<UserPermissionChange> changes) => [.. changes.Select(c => c.Permission).Order(StringComparer.Ordinal)];

    /// <summary>Identity reports problems (weak password, duplicate name) as IdentityResult errors; surface them on a field.</summary>
    private static void ThrowIfFailed(IdentityResult result, string field)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException(field, string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
