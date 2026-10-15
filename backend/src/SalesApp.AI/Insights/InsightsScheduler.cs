using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Insights;

/// <summary>Settings for the insights run.</summary>
public sealed class InsightsOptions
{
    public const string SectionName = "Ai:Insights";

    /// <summary>Recompute the insights once a day. Off in tests.</summary>
    public bool NightlyEnabled { get; set; } = true;

    /// <summary>Time of day (India) after which the day's run happens, as HH:mm.</summary>
    public string NightlyAt { get; set; } = "06:30";
}

/// <summary>
/// Runs the insights once a day, after <see cref="InsightsOptions.NightlyAt"/> India time. It checks every few
/// minutes rather than sleeping until a fixed moment, so a server that was off at that time catches up when it
/// starts. Runs as <see cref="SystemUser"/>; its AI usage is logged without a user.
/// </summary>
public sealed class InsightsScheduler(
    IServiceScopeFactory scopes,
    IBusinessClock clock,
    TimeProvider time,
    IOptionsMonitor<InsightsOptions> options,
    ILogger<InsightsScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app a moment to finish starting (migrations, seeding) before the first check.
        await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken);
        using var timer = new PeriodicTimer(CheckEvery, time);
        do
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The nightly insights run failed; it will be tried again at the next check");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.NightlyEnabled)
        {
            return false;
        }

        var at = TimeOnly.TryParseExact(settings.NightlyAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : new TimeOnly(6, 30);
        var dueLocal = clock.Today.ToDateTime(at);
        var dueUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dueLocal, DateTimeKind.Unspecified), clock.TimeZone);
        if (time.GetUtcNow().UtcDateTime < dueUtc)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().ActAs(SystemUser.Instance);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.InsightRuns.AnyAsync(r => r.CreatedAtUtc >= dueUtc, cancellationToken))
        {
            return false;
        }

        logger.LogInformation("Running the nightly insights");
        await scope.ServiceProvider.GetRequiredService<IInsightsService>().RunAsync(InsightTrigger.Nightly, cancellationToken);
        return true;
    }
}
