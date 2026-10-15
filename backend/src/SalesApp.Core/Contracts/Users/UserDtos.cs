using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;

namespace SalesApp.Core.Contracts.Users;

public sealed record UserListRequest : PageRequest
{
    public string? Role { get; init; }
}

/// <summary>A user. AddedPermissions are theirs on top of their role; RemovedPermissions are their role's that they don't have.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    bool IsLockedOut,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> AddedPermissions,
    IReadOnlyList<string> RemovedPermissions);

public sealed record UserCreateRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(120)]
    public string FullName { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = string.Empty;

    /// <summary>Initial password the admin shares with the user. Minimum 8 characters with upper, lower and a digit.</summary>
    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    /// <summary>Permissions to give this user on top of their role.</summary>
    public IReadOnlyList<string> AddedPermissions { get; init; } = [];

    /// <summary>Permissions of their role to take away from this user.</summary>
    public IReadOnlyList<string> RemovedPermissions { get; init; } = [];
}

public sealed record UserUpdateRequest
{
    [Required, MaxLength(120)]
    public string FullName { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    /// <summary>Permissions to give this user on top of their role.</summary>
    public IReadOnlyList<string> AddedPermissions { get; init; } = [];

    /// <summary>Permissions of their role to take away from this user.</summary>
    public IReadOnlyList<string> RemovedPermissions { get; init; } = [];
}

public sealed record ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>A menu in the permission list: its View permission, and its Add &amp; change one if it changes data.</summary>
public sealed record PermissionMenuDto(string Section, string Name, string View, string ViewDescription, string? Edit, string? EditDescription);

/// <summary>A right that isn't a menu, such as seeing cost prices.</summary>
public sealed record PermissionRightDto(string Key, string Name, string Description);

/// <summary>The permissions to choose from. Needs maps a permission to the ones granting it also grants.</summary>
public sealed record PermissionCatalogDto(
    IReadOnlyList<PermissionMenuDto> Menus,
    IReadOnlyList<PermissionRightDto> SpecialRights,
    IReadOnlyDictionary<string, string[]> Needs);

/// <summary>
/// A role. IsSystem marks Administrator (every permission; it can't be changed or deleted). IsBuiltIn marks the roles
/// the app comes with (their names are fixed and they can't be deleted). UserCount counts active and inactive users.
/// </summary>
public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool IsBuiltIn, int UserCount, IReadOnlyList<string> Permissions);

public sealed record RoleSaveRequest
{
    [Required, MinLength(2), MaxLength(50)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; init; }

    public IReadOnlyList<string> Permissions { get; init; } = [];
}
