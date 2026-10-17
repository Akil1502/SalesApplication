namespace SalesApp.Core.Entities;

public enum NotificationKind
{
    PaymentReceived,
    OrderCreated,
    OrderConfirmed,
    OverdueSummary,
    LowStock,
    PurchaseOrderApproval,
    BillApproval,
    InsightsReady,
}

/// <summary>
/// Something worth telling people about, recorded once (not once per person): who may see it is worked out when
/// each person looks, from its kind (which permission), its customer (whose sales rep) and who caused it (nobody
/// is told about their own actions). Kept for 90 days.
/// </summary>
public sealed class Notification
{
    public long Id { get; set; }
    public NotificationKind Kind { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }

    /// <summary>Where it leads in the app, e.g. /invoices/42.</summary>
    public required string Link { get; set; }

    /// <summary>The customer it's about: people without "Hear about every customer" see it only if they're that customer's rep.</summary>
    public int? CustomerId { get; set; }

    /// <summary>Who caused it, so they aren't told about it; null for things nobody did (stock running low, the daily summary).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>For one person only (the daily overdue summary is worked out per person); null for everyone allowed.</summary>
    public Guid? RecipientUserId { get; set; }

    /// <summary>Stops the same thing being recorded twice (one overdue summary per person per day).</summary>
    public string? DedupKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// What one person has read and muted. Everything up to ReadUpToId is read; later ones read one at a time are in
/// <see cref="NotificationRead"/>, which "Mark all read" clears.
/// </summary>
public sealed class UserNotificationState
{
    public Guid UserId { get; set; }
    public long ReadUpToId { get; set; }

    /// <summary>The newest notification there was when the bell was last opened: later ones are "since your last visit".</summary>
    public long LastOpenedUpToId { get; set; }

    /// <summary>Kinds this person has switched off, comma-separated.</summary>
    public string? MutedKinds { get; set; }
}

public sealed class NotificationRead
{
    public Guid UserId { get; set; }
    public long NotificationId { get; set; }
}
