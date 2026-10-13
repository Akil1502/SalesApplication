namespace SalesApp.Core.Security;

/// <summary>
/// Role names used in JWT role claims and [Authorize(Roles = ...)] attributes.
/// Kept as string constants so they can be used inside attributes.
/// </summary>
public static class Roles
{
    /// <summary>Full access, including user management and company settings.</summary>
    public const string Admin = "Admin";

    /// <summary>All sales data, reports, margins and AI insights; no user management.</summary>
    public const string SalesManager = "SalesManager";

    /// <summary>Day-to-day order entry and customer work.</summary>
    public const string SalesRep = "SalesRep";

    /// <summary>Comma-joined forms for attribute use.</summary>
    public const string AdminOrManager = Admin + "," + SalesManager;

    public static readonly IReadOnlyList<string> All = [Admin, SalesManager, SalesRep];
}
