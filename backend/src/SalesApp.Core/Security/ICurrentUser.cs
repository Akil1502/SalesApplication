namespace SalesApp.Core.Security;

/// <summary>
/// Who is making the current request. Services use it for role-dependent rules (for example,
/// sales reps can't change credit limits) without depending on ASP.NET Core.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    bool IsInRole(string role);

    bool IsAdmin => IsInRole(Roles.Admin);

    bool IsManagerOrAdmin => IsInRole(Roles.Admin) || IsInRole(Roles.SalesManager);
}
