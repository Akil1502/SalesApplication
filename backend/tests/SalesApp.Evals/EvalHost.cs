using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI.Claude;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.DemoData;

namespace SalesApp.Evals;

/// <summary>Whether this run may call the real Claude API, and what it's expected to cost.</summary>
public static class EvalSettings
{
    public static bool Enabled => string.Equals(Environment.GetEnvironmentVariable("SALESAPP_RUN_EVALS"), "yes", StringComparison.OrdinalIgnoreCase);

    public const string SkipReason =
        "Live evals call the Claude API and cost money (about ₹50 for both suites). Set SALESAPP_RUN_EVALS=yes to run them.";

    /// <summary>The run stops when this much has been spent (the gateway's daily budget for the eval user).</summary>
    public const decimal BudgetUsd = 3m;

    /// <summary>
    /// The case ids in SALESAPP_EVAL_CASES (comma-separated), to re-check a few cases without paying for the whole
    /// suite. Unset means every case.
    /// </summary>
    public static IReadOnlySet<string> Cases { get; } =
        (Environment.GetEnvironmentVariable("SALESAPP_EVAL_CASES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool Selected(string id) => Cases.Count == 0 || Cases.Contains(id);
}

/// <summary>The person the evals act as: a sales manager, so the margins view is available too.</summary>
public sealed class EvalUser(Guid id) : ICurrentUser
{
    public Guid UserId => id;

    public bool IsInRole(string role) => role == Roles.SalesManager;
}

/// <summary>
/// Hosts the real API with the Development settings (so it reads the Claude key from user-secrets) against a
/// throwaway database filled with the standard 24 months of demo data, created only when evals are enabled and
/// dropped afterwards. Background jobs are off and the spend is capped at <see cref="EvalSettings.BudgetUsd"/>.
/// </summary>
public class EvalHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"SalesApp_Eval_{DateTime.UtcNow:yyyyMMddHHmm}_{Guid.NewGuid():N}"[..40];
    private bool _created;

    public Guid UserId { get; private set; }

    private static string SqlServer =>
        Environment.GetEnvironmentVariable("SALESAPP_TEST_SERVER") is { Length: > 0 } custom ? custom : @"(localdb)\MSSQLLocalDB";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", $"Server={SqlServer};Database={_database};Trusted_Connection=True;TrustServerCertificate=True");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Ai:Insights:NightlyEnabled", "false");
        builder.UseSetting("Ai:DailyBudgetUsdPerUser", EvalSettings.BudgetUsd.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Whether to create the database: only for a live run, unless a subclass always needs it.</summary>
    protected virtual bool CreateDatabase => EvalSettings.Enabled;

    public async ValueTask InitializeAsync()
    {
        if (!CreateDatabase)
        {
            return;
        }

        _created = true;
        await DbInitializer.InitializeAsync(Services, CancellationToken.None);
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataGenerator>().GenerateAsync(new DemoDataOptions(Months: 24), CancellationToken.None);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = "evals@salesapp.test", Email = "evals@salesapp.test", FullName = "Eval run", CreatedAtUtc = DateTime.UtcNow };
        var created = await users.CreateAsync(user, $"Eval-{Guid.NewGuid():N}-Aa1");
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, Roles.SalesManager);
        UserId = user.Id;
    }

    /// <summary>A DI scope acting as the eval user.</summary>
    public AsyncServiceScope Scope()
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().ActAs(new EvalUser(UserId));
        return scope;
    }

    /// <summary>What the eval user's Claude calls have cost so far, in US dollars.</summary>
    public async Task<(decimal CostUsd, int Calls)> SpendAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.AiUsageLogs.Where(l => l.UserId == UserId).Select(l => l.CostUsd).ToListAsync();
        return (logs.Sum(), logs.Count);
    }

    public bool ClaudeAvailable
    {
        get
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IClaudeGateway>().IsAvailable;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (_created)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>The same database for checks that don't call Claude, so it's always created.</summary>
public sealed class ReferenceQueryHost : EvalHost
{
    protected override bool CreateDatabase => true;
}
