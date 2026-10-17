using Microsoft.AspNetCore.Mvc;
using SalesApp.Core.Contracts.Notifications;
using SalesApp.Infrastructure.Notifications;

namespace SalesApp.Api.Controllers;

/// <summary>
/// The signed-in person's notifications. Open to everyone signed in: what each person sees follows their
/// permissions, their customers and their own switches (see <see cref="NotificationService"/>).
/// </summary>
public sealed class NotificationsController(INotificationService notifications) : ApiControllerBase
{
    /// <param name="take">How many, newest first (at most 100).</param>
    /// <param name="unreadOnly">Only the unread ones.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet]
    [ProducesResponseType<NotificationListDto>(StatusCodes.Status200OK)]
    public Task<NotificationListDto> List([FromQuery] int take = 30, [FromQuery] bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        return notifications.ListAsync(take, unreadOnly, cancellationToken);
    }

    [HttpPost("{id:long}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(NotificationMarkRequest request, CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(request.UpToId, cancellationToken);
        return NoContent();
    }

    /// <summary>The bell was opened; what's there now is no longer "since your last visit".</summary>
    [HttpPost("opened")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Opened(NotificationMarkRequest request, CancellationToken cancellationToken)
    {
        await notifications.OpenedAsync(request.UpToId, cancellationToken);
        return NoContent();
    }

    /// <summary>The kinds this person may receive, and which they've switched off.</summary>
    [HttpGet("preferences")]
    [ProducesResponseType<IReadOnlyList<NotificationKindDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<NotificationKindDto>> Preferences(CancellationToken cancellationToken) => notifications.GetPreferencesAsync(cancellationToken);

    [HttpPut("preferences")]
    [ProducesResponseType<IReadOnlyList<NotificationKindDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<Errors.ApiError>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<NotificationKindDto>> SavePreferences(NotificationPreferencesRequest request, CancellationToken cancellationToken) =>
        notifications.SavePreferencesAsync(request.Muted, cancellationToken);
}
