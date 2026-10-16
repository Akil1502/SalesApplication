namespace SalesApp.Core.Entities;

/// <summary>What an audit entry records.</summary>
public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    SignedIn,
    SignInFailed,
    LockedOut,
    PasswordReset,
    PermissionsChanged,
    Exported,
}

/// <summary>
/// One line of the audit trail: who did what to which record, when, from where, and for changes the old and new
/// value of every field. Entries are append-only (a database trigger refuses updates and deletes), so the trail can
/// be trusted after the fact.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Null for background work and for failed sign-ins.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The user's name at the time ("System" for background work; the email tried for a failed sign-in).</summary>
    public required string UserName { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>The kind of record, such as "Customer" or "Invoice"; null for events that aren't about one record.</summary>
    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /// <summary>How the record was known at the time, such as the customer's name or the invoice number.</summary>
    public string? EntityLabel { get; set; }

    /// <summary>The changed fields as JSON: [{ "field": "Credit limit", "old": "₹5,00,000", "new": "₹7,50,000" }].</summary>
    public string? ChangesJson { get; set; }

    /// <summary>A sentence for events without field changes, such as "Downloaded the GST summary report".</summary>
    public string? Summary { get; set; }

    public string? IpAddress { get; set; }
}
