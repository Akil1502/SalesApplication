namespace SalesApp.Core.Security;

/// <summary>
/// The built-in roles. Administrators can add their own roles in the app; a role is a set of
/// <see cref="Permissions"/>, and access is always checked by permission, never by role name.
/// </summary>
public static class Roles
{
    /// <summary>Every permission, always. It can't be changed or deleted, and there is always one active administrator.</summary>
    public const string Admin = "Administrator";

    /// <summary>Starts with everything except administration (see <see cref="Permissions.SalesManagerDefaults"/>).</summary>
    public const string SalesManager = "Sales manager";

    /// <summary>Starts with day-to-day order and customer work (see <see cref="Permissions.SalesRepDefaults"/>).</summary>
    public const string SalesRep = "Sales representative";

    public static readonly IReadOnlyList<string> BuiltIn = [Admin, SalesManager, SalesRep];

    /// <summary>What a built-in role is for, shown when choosing a role.</summary>
    public static string? Description(string role) => role switch
    {
        Admin => "Everything, including users, roles and company settings.",
        SalesManager => "All sales work, products, credit terms, margins and AI insights. No administration.",
        SalesRep => "Customers, orders and invoices. No cost prices or credit terms.",
        _ => null,
    };

    /// <summary>The permissions a built-in role is created with; an administrator can change them afterwards.</summary>
    public static IReadOnlySet<string> DefaultPermissions(string role) => role switch
    {
        Admin => Permissions.All,
        SalesManager => Permissions.SalesManagerDefaults,
        SalesRep => Permissions.SalesRepDefaults,
        _ => new HashSet<string>(),
    };
}
