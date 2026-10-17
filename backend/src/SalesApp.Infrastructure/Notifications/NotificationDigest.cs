using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Security;

namespace SalesApp.Infrastructure.Notifications;

/// <summary>Settings for the morning notifications, bound from the "Notifications" section.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Send the morning overdue summary and clear out old notifications. Off in tests.</summary>
    public bool DailyEnabled { get; set; } = true;

    /// <summary>Time of day (India) after which the morning summary is written, as HH:mm.</summary>
    public string DailyAt { get; set; } = "09:00";
}

public interface INotificationDigest
{
    /// <summary>
    /// Writes each person's overdue summary for the day (once: running it again changes nothing) and clears out
    /// notifications older than 90 days. Returns how many summaries were written.
    /// </summary>
    Task<int> RunAsync(DateOnly day, CancellationToken cancellationToken);
}

/// <summary>
/// The morning summary of overdue invoices, worked out per person: everyone who can see invoices gets the count and
/// total of those past their due date, for every customer or only the customers they look after.
/// </summary>
public sealed class NotificationDigest(AppDbContext db, IPermissionService permissions, INotificationWriter writer, TimeProvider time) : INotificationDigest
{
    private static readonly InvoiceStatus[] Open = [InvoiceStatus.Unpaid, InvoiceStatus.PartiallyPaid];

    public async Task<int> RunAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var written = 0;
        var users = await db.Users.AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        foreach (var userId in users)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"overdue:{userId:N}:{day:yyyyMMdd}");
            if (await permissions.GetAsync(userId, cancellationToken) is not { } access
                || !access.Permissions.Contains(Permissions.InvoicesView)
                || await db.Notifications.AnyAsync(n => n.DedupKey == key, cancellationToken))
            {
                continue;
            }

            var overdue = db.Invoices.AsNoTracking().Where(i => Open.Contains(i.Status) && i.DueDate < day);
            if (!access.Permissions.Contains(Permissions.AllCustomersActivity))
            {
                overdue = overdue.Where(i => i.Customer!.AssignedRepId == userId);
            }

            var summary = await overdue
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Total = g.Sum(i => i.GrandTotal - i.AmountPaid - i.AmountCredited) })
                .SingleOrDefaultAsync(cancellationToken);
            if (summary is not { Count: > 0 })
            {
                continue;
            }

            var oldest = await overdue.OrderBy(i => i.DueDate).ThenBy(i => i.Id).Select(i => new { i.Number, i.CustomerName, i.DueDate }).FirstAsync(cancellationToken);
            var late = day.DayNumber - oldest.DueDate.DayNumber;
            await writer.RaiseAsync(new NotificationEvent(
                NotificationKind.OverdueSummary,
                summary.Count == 1 ? "1 invoice is overdue" : string.Create(CultureInfo.InvariantCulture, $"{summary.Count} invoices are overdue"),
                string.Create(CultureInfo.InvariantCulture,
                    $"{AuditFormat.Money(summary.Total)} in all. The oldest, {oldest.Number} for {oldest.CustomerName}, is {late} {(late == 1 ? "day" : "days")} late."),
                "/invoices?view=overdue",
                ByCurrentUser: false,
                RecipientUserId: userId,
                DedupKey: key), cancellationToken);
            written++;
        }

        var cutoff = time.GetUtcNow().UtcDateTime - NotificationService.KeptFor;
        await db.Notifications.Where(n => n.CreatedAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
        return written;
    }
}

/// <summary>
/// Runs the morning summary once a day after <see cref="NotificationOptions.DailyAt"/> India time. It checks every few
/// minutes, so a server that was off at that time catches up when it starts; each person's summary is recorded
/// once per day however often it runs.
/// </summary>
public sealed class NotificationScheduler(
    IServiceScopeFactory scopes,
    IBusinessClock clock,
    TimeProvider time,
    IOptionsMonitor<NotificationOptions> options,
    ILogger<NotificationScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);
    private DateOnly? _doneFor;

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
                logger.LogError(exception, "The morning notifications failed; they will be tried again at the next check");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunIfDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var today = clock.Today;
        if (!settings.DailyEnabled || _doneFor == today)
        {
            return;
        }

        var at = TimeOnly.TryParseExact(settings.DailyAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : new TimeOnly(9, 0);
        var dueUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today.ToDateTime(at), DateTimeKind.Unspecified), clock.TimeZone);
        if (time.GetUtcNow().UtcDateTime < dueUtc)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().ActAs(SystemUser.Instance);
        var written = await scope.ServiceProvider.GetRequiredService<INotificationDigest>().RunAsync(today, cancellationToken);
        _doneFor = today;
        logger.LogInformation("Morning notifications: {Count} overdue summaries", written);
    }
}
