using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Notifications;

/// <summary>Something to tell people about. Who may see it is decided when they look (see <see cref="NotificationService"/>).</summary>
/// <param name="Kind">What sort of thing happened; decides who may see it.</param>
/// <param name="Title">One line, e.g. "₹50,000.00 received from Pandian Knits".</param>
/// <param name="Body">A second line with the detail, if any.</param>
/// <param name="Link">Where it leads in the app, e.g. /invoices/42.</param>
/// <param name="CustomerId">The customer it's about, for people who hear only about their own customers.</param>
/// <param name="ByCurrentUser">
/// Whether the person making the request caused it (they aren't told about it). Off for consequences nobody chose,
/// such as stock running low.
/// </param>
/// <param name="RecipientUserId">For one person only; null for everyone allowed.</param>
/// <param name="DedupKey">Recorded once only under this key.</param>
public sealed record NotificationEvent(
    NotificationKind Kind,
    string Title,
    string? Body,
    string Link,
    int? CustomerId = null,
    bool ByCurrentUser = true,
    Guid? RecipientUserId = null,
    string? DedupKey = null);

public interface INotificationWriter
{
    /// <summary>
    /// Records it straight away: call it after the change it's about has been saved, inside the same transaction
    /// where there is one, so the two are committed together.
    /// </summary>
    Task RaiseAsync(NotificationEvent notification, CancellationToken cancellationToken);
}

public sealed class NotificationWriter(AppDbContext db, ICurrentUser currentUser, TimeProvider time) : INotificationWriter
{
    private static readonly AsyncLocal<int> SuppressedDepth = new();

    /// <summary>Turns notifications off for the current flow until disposed: for bulk set-up work such as demo data.</summary>
    public static IDisposable Suppress()
    {
        SuppressedDepth.Value++;
        return new Restore();
    }

    public async Task RaiseAsync(NotificationEvent notification, CancellationToken cancellationToken)
    {
        if (SuppressedDepth.Value > 0)
        {
            return;
        }

        db.Notifications.Add(new Notification
        {
            Kind = notification.Kind,
            Title = Truncate(notification.Title, 200)!,
            Body = Truncate(notification.Body, 400),
            Link = notification.Link,
            CustomerId = notification.CustomerId,
            ActorUserId = notification.ByCurrentUser && !currentUser.IsSystem ? currentUser.UserId : null,
            RecipientUserId = notification.RecipientUserId,
            DedupKey = notification.DedupKey,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? text, int length) => text is { Length: var n } && n > length ? text[..(length - 1)] + "…" : text;

    private sealed class Restore : IDisposable
    {
        public void Dispose() => SuppressedDepth.Value--;
    }
}
