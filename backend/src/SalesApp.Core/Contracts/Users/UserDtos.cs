using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;

namespace SalesApp.Core.Contracts.Users;

public sealed record UserListRequest : PageRequest
{
    public string? Role { get; init; }
}

public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    bool IsLockedOut,
    DateTime CreatedAtUtc);

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
}

public sealed record UserUpdateRequest
{
    [Required, MaxLength(120)]
    public string FullName { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}

public sealed record ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}
