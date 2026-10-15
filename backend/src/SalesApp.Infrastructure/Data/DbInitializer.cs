using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.Infrastructure.Data;

/// <summary>
/// Brings the database up to date and makes sure the roles and the bootstrap admin exist.
/// Every step is idempotent, so it is safe to run on every startup.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

        var db = provider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await DbObjectsInitializer.ApplyAsync(db, cancellationToken);

        await SeedRolesAsync(provider.GetRequiredService<RoleManager<AppRole>>(), logger);
        await SeedAdminAsync(provider, logger);
    }

    /// <summary>
    /// Creates any missing built-in role with its default permissions. A role that already exists is left alone, so
    /// changes an administrator made to its permissions survive restarts.
    /// </summary>
    private static async Task SeedRolesAsync(RoleManager<AppRole> roleManager, ILogger logger)
    {
        foreach (var name in Roles.BuiltIn)
        {
            if (await roleManager.RoleExistsAsync(name))
            {
                continue;
            }

            var system = name == Roles.Admin;
            var role = new AppRole(name)
            {
                Description = Roles.Description(name),
                IsSystem = system,
                Permissions = system ? [] : [.. Roles.DefaultPermissions(name).Select(p => new RoleGrant { Permission = p })],
            };
            EnsureSucceeded(await roleManager.CreateAsync(role), $"create role '{name}'");
            logger.LogInformation("Created role {Role}", name);
        }
    }

    private static async Task SeedAdminAsync(IServiceProvider provider, ILogger logger)
    {
        var options = provider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var userManager = provider.GetRequiredService<UserManager<AppUser>>();

        // Only bootstrap when there is no admin at all; after that, admins manage users in the app.
        if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            logger.LogWarning(
                "No admin user exists and Seed:AdminPassword is not configured, so none was created. " +
                "Set it with: dotnet user-secrets set \"Seed:AdminPassword\" \"<password>\"");
            return;
        }

        var admin = new AppUser
        {
            UserName = options.AdminEmail,
            Email = options.AdminEmail,
            EmailConfirmed = true,
            FullName = options.AdminFullName,
            CreatedAtUtc = provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime,
        };

        EnsureSucceeded(await userManager.CreateAsync(admin, options.AdminPassword), "create the admin user");
        EnsureSucceeded(await userManager.AddToRoleAsync(admin, Roles.Admin), "add the admin user to the Administrator role");
        logger.LogInformation("Created bootstrap admin {Email}", options.AdminEmail);
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            var reasons = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Database seeding failed to {action}: {reasons}");
        }
    }
}
