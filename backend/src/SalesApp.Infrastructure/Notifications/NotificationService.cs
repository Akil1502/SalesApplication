using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Contracts.Notifications;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Notifications;

public interface INotificationService
{
    Task<NotificationListDto> ListAsync(int take, bool unreadOnly, CancellationToken cancellationToken);
    Task MarkReadAsync(long id, CancellationToken cancellationToken);
    Task MarkAllReadAsync(long upToId, CancellationToken cancellationToken);

    /// <summary>The bell was opened: what's there now is no longer "since your last visit".</summary>
    Task OpenedAsync(long upToId, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationKindDto>> GetPreferencesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationKindDto>> SavePreferencesAsync(IReadOnlyList<NotificationKind> muted, CancellationToken cancellationToken);
}

/// <summary>
/// Each person's notifications, worked out when they look: the kinds their permissions allow and they haven't
/// switched off; about their own customers unless they may hear about every customer; never their own actions; and
/// from the last 90 days.
/// </summary>
public sealed class NotificationService(AppDbContext db, ICurrentUser currentUser, TimeProvider time) : INotificationService
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(90);

    /// <summary>Someone using the bell for the first time finds the last day's notifications unread, not three months'.</summary>
    private static readonly TimeSpan FreshForNewcomers = TimeSpan.FromDays(1);

    public async Task<NotificationListDto> ListAsync(int take, bool unreadOnly, CancellationToken cancellationToken)
    {
        var state = await StateAsync(cancellationToken);
        var visible = Visible(state);
        var unread = visible.Where(n => n.Id > state.ReadUpToId && !db.NotificationReads.Any(r => r.UserId == state.UserId && r.NotificationId == n.Id));

        var important = NotificationKinds.All.Where(k => k.Important).Select(k => k.Kind).ToList();
        var items = await (unreadOnly ? unread : visible)
            .OrderByDescending(n => n.Id)
            .Take(Math.Clamp(take, 1, 100))
            .Select(n => new NotificationDto(
                n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAtUtc,
                n.Id <= state.ReadUpToId || db.NotificationReads.Any(r => r.UserId == state.UserId && r.NotificationId == n.Id),
                important.Contains(n.Kind)))
            .ToListAsync(cancellationToken);

        return new NotificationListDto(items, await unread.CountAsync(cancellationToken), state.LastOpenedUpToId);
    }

    public async Task MarkReadAsync(long id, CancellationToken cancellationToken)
    {
        var state = await StateAsync(cancellationToken);
        if (id <= state.ReadUpToId || !await Visible(state).AnyAsync(n => n.Id == id, cancellationToken))
        {
            return;
        }

        if (!await db.NotificationReads.AnyAsync(r => r.UserId == state.UserId && r.NotificationId == id, cancellationToken))
        {
            db.NotificationReads.Add(new NotificationRead { UserId = state.UserId, NotificationId = id });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Marked read in another tab at the same moment: that's what was wanted.
            }
        }
    }

    public async Task MarkAllReadAsync(long upToId, CancellationToken cancellationToken)
    {
        var state = await StateAsync(cancellationToken);
        if (upToId <= state.ReadUpToId)
        {
            return;
        }

        state.ReadUpToId = upToId;
        await db.SaveChangesAsync(cancellationToken);
        await db.NotificationReads.Where(r => r.UserId == state.UserId && r.NotificationId <= upToId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task OpenedAsync(long upToId, CancellationToken cancellationToken)
    {
        var state = await StateAsync(cancellationToken);
        if (upToId > state.LastOpenedUpToId)
        {
            state.LastOpenedUpToId = upToId;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<NotificationKindDto>> GetPreferencesAsync(CancellationToken cancellationToken)
    {
        var muted = Muted(await StateAsync(cancellationToken));
        return [.. NotificationKinds.AllowedFor(currentUser).Select(k => new NotificationKindDto(k.Kind, k.Label, k.Description, muted.Contains(k.Kind)))];
    }

    public async Task<IReadOnlyList<NotificationKindDto>> SavePreferencesAsync(IReadOnlyList<NotificationKind> muted, CancellationToken cancellationToken)
    {
        var state = await StateAsync(cancellationToken);
        var kinds = muted.Distinct().Order().ToList();
        state.MutedKinds = kinds.Count == 0 ? null : string.Join(',', kinds);
        await db.SaveChangesAsync(cancellationToken);
        return await GetPreferencesAsync(cancellationToken);
    }

    private static HashSet<NotificationKind> Muted(UserNotificationState state) =>
        (state.MutedKinds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => Enum.TryParse<NotificationKind>(k, out var kind) ? kind : (NotificationKind?)null)
            .OfType<NotificationKind>()
            .ToHashSet();

    private IQueryable<Notification> Visible(UserNotificationState state)
    {
        var userId = state.UserId;
        var muted = Muted(state);
        var kinds = NotificationKinds.AllowedFor(currentUser).Select(k => k.Kind).Where(k => !muted.Contains(k)).ToList();
        var aboutCustomers = NotificationKinds.All.Where(k => k.AboutCustomer).Select(k => k.Kind).ToList();
        var since = time.GetUtcNow().UtcDateTime - KeptFor;
        var everyCustomer = currentUser.Has(Permissions.AllCustomersActivity);

        var query = db.Notifications.AsNoTracking().Where(n =>
            kinds.Contains(n.Kind)
            && n.CreatedAtUtc >= since
            && (n.RecipientUserId == null || n.RecipientUserId == userId)
            && (n.ActorUserId == null || n.ActorUserId != userId));

        if (!everyCustomer)
        {
            // Only the customers this person looks after.
            query = query.Where(n => !aboutCustomers.Contains(n.Kind) || db.Customers.Any(c => c.Id == n.CustomerId && c.AssignedRepId == userId));
        }

        return query;
    }

    private async Task<UserNotificationState> StateAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (await db.UserNotificationStates.SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken) is { } state)
        {
            return state;
        }

        var freshFrom = time.GetUtcNow().UtcDateTime - FreshForNewcomers;
        var readUpTo = await db.Notifications.Where(n => n.CreatedAtUtc < freshFrom).MaxAsync(n => (long?)n.Id, cancellationToken) ?? 0;
        state = new UserNotificationState { UserId = userId, ReadUpToId = readUpTo, LastOpenedUpToId = readUpTo };
        db.UserNotificationStates.Add(state);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateException)
        {
            // Created by another request at the same moment: use that one.
            db.Entry(state).State = EntityState.Detached;
            return await db.UserNotificationStates.SingleAsync(s => s.UserId == userId, cancellationToken);
        }
    }
}
