using Microsoft.AspNetCore.Identity;

namespace SalesApp.Core.Entities;

/// <summary>
/// A role: a named set of permissions that administrators can create and change. The built-in Administrator role
/// is a system role that holds every permission without storing them, and can't be changed or deleted.
/// </summary>
public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
    }

    public AppRole(string name)
        : base(name)
    {
    }

    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    public List<RoleGrant> Permissions { get; set; } = [];
}

/// <summary>One permission held by a role (a key from SalesApp.Core.Security.Permissions).</summary>
public sealed class RoleGrant
{
    public Guid RoleId { get; set; }

    public required string Permission { get; set; }
}

/// <summary>
/// A change to one user's permissions relative to their role: granted on top of it, or taken away. A user's
/// permissions are their role's, plus the granted ones, minus the taken-away ones.
/// </summary>
public sealed class UserPermissionChange
{
    public Guid UserId { get; set; }

    public required string Permission { get; set; }

    public bool IsGranted { get; set; }
}
