using System.ComponentModel.DataAnnotations;

namespace SalesApp.Core.Contracts.Auth;

// Request DTOs use [Required] + default values rather than the C# `required` keyword:
// `required` makes System.Text.Json reject the whole body before validation runs, so the
// client would get one vague error instead of a message per field.

public sealed record LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    CurrentUserDto User);

/// <summary>
/// The signed-in user: their role name (for display) and what they may do (keys from
/// SalesApp.Core.Security.Permissions, after any changes made for them alone).
/// </summary>
public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    IReadOnlyList<string> Permissions);
