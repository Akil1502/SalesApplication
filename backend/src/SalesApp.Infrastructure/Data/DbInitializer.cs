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

        await SeedRolesAsync(provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), logger);
        await SeedAdminAsync(provider, logger);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager, ILogger logger)
    {
        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            EnsureSucceeded(result, $"create role '{role}'");
            logger.LogInformation("Created role {Role}", role);
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
        EnsureSucceeded(await userManager.AddToRoleAsync(admin, Roles.Admin), "add the admin user to the Admin role");
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
