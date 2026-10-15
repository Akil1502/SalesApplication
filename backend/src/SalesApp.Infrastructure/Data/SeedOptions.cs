namespace SalesApp.Infrastructure.Data;

/// <summary>
/// Bootstrap admin account, bound from the "Seed" configuration section.
/// Development ships a default in appsettings.Development.json; any other environment must
/// supply the password through user-secrets or environment variables, or no admin is created.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@salesapp.local";

    public string AdminFullName { get; set; } = "System Administrator";

    public string? AdminPassword { get; set; }
}
