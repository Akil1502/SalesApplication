using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database context, ASP.NET Core Identity (users, roles, password hashing,
    /// lockout) and seeding options. Token issuing lives in the API project.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        // No EnableRetryOnFailure: invoicing uses explicit transactions (gapless numbering),
        // which a retrying execution strategy would reject. LocalDB/on-prem SQL rarely needs it.
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        // AddIdentityCore (not AddIdentity) because the API authenticates with JWTs, not cookies.
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        return services;
    }
}
