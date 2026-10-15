namespace SalesApp.Core.Security;

/// <summary>
/// Who is making the current request. Services use it for permission-dependent rules (for example, only users who
/// may set credit terms can change a credit limit) without depending on ASP.NET Core.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    /// <summary>Whether the user holds this permission (a key from <see cref="Permissions"/>).</summary>
    bool Has(string permission);

    /// <summary>The user's role name, for display (the assistant tells Claude who it is helping).</summary>
    string RoleName { get; }

    /// <summary>True for background work (the nightly insights run), which acts for the company, not a person.</summary>
    bool IsSystem => false;
}
