using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Infrastructure.Data;

[assembly: AssemblyFixture(typeof(SalesApp.IntegrationTests.SalesAppFactory))]

namespace SalesApp.IntegrationTests;

/// <summary>
/// Hosts the real API in memory against a brand-new LocalDB database for this test run,
/// then drops the database afterwards. Shared by every test in the assembly.
/// </summary>
public sealed class SalesAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@salesapp.test";
    public const string AdminPassword = "Integration-Test-Admin-1";

    private readonly string _databaseName = $"SalesApp_Test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" skips appsettings.Development.json and user-secrets, so the run is fully
        // determined by the settings below. UseSetting (rather than ConfigureAppConfiguration)
        // makes the values visible while Program.cs is still registering services.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default",
            $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-a-secret-0123456789");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }

    public async ValueTask InitializeAsync()
    {
        // Explicit rather than relying on MigrateOnStartup, so setup order is obvious and awaited.
        await DbInitializer.InitializeAsync(Services, TestContext.Current.CancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }
}
