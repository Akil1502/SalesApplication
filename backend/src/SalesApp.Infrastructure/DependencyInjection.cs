using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database context, ASP.NET Core Identity (users, roles, password hashing,
    /// lockout), seeding options and the application services. Token issuing lives in the API project.
    /// The host must also register an <see cref="Core.Security.ICurrentUser"/> implementation.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddSingleton<AuditTimestampsInterceptor>();

        // No EnableRetryOnFailure: invoicing uses explicit transactions (gapless numbering),
        // which a retrying execution strategy would reject. LocalDB/on-prem SQL rarely needs it.
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseSqlServer(connectionString)
            .AddInterceptors(provider.GetRequiredService<AuditTimestampsInterceptor>()));

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

        services.AddMemoryCache();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IHsnCodeService, HsnCodeService>();
        services.AddScoped<ICompanySettingsService, CompanySettingsService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IUserAdminService, UserAdminService>();

        return services;
    }
}
