using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Security;

namespace SalesApp.Infrastructure.Services;

public interface IRoleAdminService
{
    PermissionCatalogDto Catalog();
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken);
    Task<RoleDto> CreateAsync(RoleSaveRequest request, CancellationToken cancellationToken);
    Task<RoleDto> UpdateAsync(Guid id, RoleSaveRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Roles as permission sets. Administrator always holds everything and can't be changed. The other built-in roles
/// keep their names (the demo data and the setup rely on them) but their permissions can be changed. An administrator
/// can't change a role in a way that takes Users &amp; roles away from themselves.
/// </summary>
public sealed class RoleAdminService(
    AppDbContext db,
    RoleManager<AppRole> roles,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IAuditLog audit) : IRoleAdminService
{
    public PermissionCatalogDto Catalog() => new(Permissions.Menus, Permissions.SpecialRights, Permissions.NeedsByPermission);

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await db.Roles.AsNoTracking()
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                Users = db.UserRoles.Count(ur => ur.RoleId == r.Id),
                Permissions = r.Permissions.Select(p => p.Permission).ToList(),
            })
            .ToListAsync(cancellationToken);

        return [.. list
            .OrderByDescending(r => r.IsSystem)
            .ThenBy(r => Roles.BuiltIn.Contains(r.Name ?? string.Empty) ? 0 : 1)
            .ThenBy(r => r.Name)
            .Select(r => ToDto(r.Id, r.Name, r.Description, r.IsSystem, r.Users, r.Permissions))];
    }

    public async Task<RoleDto> CreateAsync(RoleSaveRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, null);

        var role = new AppRole(name)
        {
            Description = Text.NullIfBlank(request.Description),
            Permissions = [.. Permissions.WithNeeds(request.Permissions).Select(p => new RoleGrant { Permission = p })],
        };
        ThrowIfFailed(await roles.CreateAsync(role));
        await AuditPermissionsAsync(role, [], role.Permissions.Select(p => p.Permission), cancellationToken);
        permissions.Invalidate();
        return await GetAsync(role.Id, cancellationToken);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, RoleSaveRequest request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Role {id} was not found.");
        if (role.IsSystem)
        {
            throw new ConflictException("The Administrator role always has every permission and can't be changed.");
        }

        var name = request.Name.Trim();
        if (Roles.BuiltIn.Contains(role.Name ?? string.Empty) && name != role.Name)
        {
            throw new ValidationException("name", "Built-in roles keep their names. Create a new role to use a different name.");
        }

        await EnsureNameFreeAsync(name, role.Id);
        var granted = Permissions.WithNeeds(request.Permissions);
        await EnsureNotLockingOutSelfAsync(role.Id, granted, cancellationToken);

        var before = role.Permissions.Select(p => p.Permission).ToList();
        role.Description = Text.NullIfBlank(request.Description);
        role.Permissions.RemoveAll(p => !granted.Contains(p.Permission));
        role.Permissions.AddRange(granted.Except(role.Permissions.Select(p => p.Permission)).Select(p => new RoleGrant { Permission = p }));
        if (name != role.Name)
        {
            ThrowIfFailed(await roles.SetRoleNameAsync(role, name));
        }

        ThrowIfFailed(await roles.UpdateAsync(role));
        await AuditPermissionsAsync(role, before, granted, cancellationToken);
        permissions.Invalidate();
        return await GetAsync(role.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Role {id} was not found.");
        if (role.IsSystem || Roles.BuiltIn.Contains(role.Name ?? string.Empty))
        {
            throw new ConflictException("Built-in roles can't be deleted.");
        }

        var users = await db.UserRoles.CountAsync(ur => ur.RoleId == id, cancellationToken);
        if (users > 0)
        {
            throw new ConflictException($"{users} {(users == 1 ? "user has" : "users have")} this role. Give them another role first.");
        }

        ThrowIfFailed(await roles.DeleteAsync(role));
        permissions.Invalidate();
    }

    /// <summary>Records which permissions a role gained and lost (nothing when they're the same).</summary>
    private async Task AuditPermissionsAsync(AppRole role, IEnumerable<string> before, IEnumerable<string> after, CancellationToken cancellationToken)
    {
        var old = before.ToHashSet(StringComparer.Ordinal);
        var now = after.ToHashSet(StringComparer.Ordinal);
        static string? Names(IEnumerable<string> keys) =>
            string.Join(", ", keys.Select(AuditFormat.Permission).Order(StringComparer.Ordinal)) is { Length: > 0 } text ? text : null;

        var changes = new List<AuditChange>();
        if (Names(now.Except(old)) is { } gained)
        {
            changes.Add(new AuditChange("Permissions added", null, gained));
        }

        if (Names(old.Except(now)) is { } lost)
        {
            changes.Add(new AuditChange("Permissions removed", lost, null));
        }

        if (changes.Count > 0)
        {
            await audit.RecordAsync(new AuditEvent(AuditAction.PermissionsChanged, "Role", role.Id.ToString(), role.Name, Changes: changes), cancellationToken);
        }
    }

    private async Task<RoleDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).Single(r => r.Id == id);

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId)
    {
        if (await roles.FindByNameAsync(name) is { } existing && existing.Id != exceptId)
        {
            throw new ValidationException("name", $"There is already a role called {existing.Name}.");
        }
    }

    /// <summary>Changing your own role must not take Users &amp; roles away from you, or nobody may be able to put it back.</summary>
    private async Task EnsureNotLockingOutSelfAsync(Guid roleId, IReadOnlySet<string> granted, CancellationToken cancellationToken)
    {
        if (!await db.UserRoles.AnyAsync(ur => ur.RoleId == roleId && ur.UserId == currentUser.UserId, cancellationToken))
        {
            return;
        }

        var changes = await db.UserPermissionChanges.AsNoTracking().Where(p => p.UserId == currentUser.UserId).ToListAsync(cancellationToken);
        var after = Permissions.Effective(granted, changes.Where(c => c.IsGranted).Select(c => c.Permission), changes.Where(c => !c.IsGranted).Select(c => c.Permission));
        if (!after.Contains(Permissions.UsersEdit))
        {
            throw new ValidationException("permissions", "This is your own role: keep Users & roles: Add & change, or you'd lose access to this page.");
        }
    }

    private static RoleDto ToDto(Guid id, string? name, string? description, bool isSystem, int users, IEnumerable<string> granted) =>
        new(id, name ?? string.Empty, description, isSystem, Roles.BuiltIn.Contains(name ?? string.Empty), users,
            isSystem ? [.. Permissions.All.Order(StringComparer.Ordinal)] : [.. granted.Order(StringComparer.Ordinal)]);

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException("name", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
