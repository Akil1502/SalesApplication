namespace SalesApp.Core.Security;

/// <summary>
/// The identity background jobs run as. It can see what an administrator sees; its AI usage is logged without a
/// user and counts against its own daily budget.
/// </summary>
public sealed class SystemUser : ICurrentUser
{
    public static readonly SystemUser Instance = new();

    private SystemUser()
    {
    }

    public Guid UserId => Guid.Empty;

    public bool Has(string permission) => true;

    public string RoleName => Roles.Admin;

    public bool IsSystem => true;
}

/// <summary>
/// Lets code without an HTTP request (a background job) say who it is acting as for the rest of its DI scope. Set it
/// right after creating the scope, before resolving services that depend on <see cref="ICurrentUser"/>.
/// </summary>
public sealed class CurrentUserOverride
{
    public ICurrentUser? User { get; private set; }

    public void ActAs(ICurrentUser user) => User = user;
}
