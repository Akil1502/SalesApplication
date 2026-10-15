using Microsoft.AspNetCore.Identity;

namespace SalesApp.Core.Entities;

/// <summary>
/// An application user. Identity supplies the credential fields (email, password hash, lockout);
/// we add what the sales app needs on top.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public required string FullName { get; set; }

    /// <summary>
    /// Deactivated users keep their history (orders they created stay attributed to them)
    /// but can no longer sign in. We never hard-delete users.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
