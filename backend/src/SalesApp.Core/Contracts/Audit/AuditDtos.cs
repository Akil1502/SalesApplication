using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;

namespace SalesApp.Core.Contracts.Audit;

/// <summary>One changed field, with its values written as people read them ("₹7,50,000", "16 Oct 2026"); null when empty.</summary>
public sealed record AuditChange(string Field, string? Old, string? New);

public sealed record AuditEntryDto(
    long Id,
    DateTime OccurredAtUtc,
    Guid? UserId,
    string UserName,
    AuditAction Action,
    string? EntityType,
    string? EntityId,
    string? EntityLabel,
    string? Summary,
    IReadOnlyList<AuditChange> Changes,
    string? IpAddress);

public sealed record AuditListRequest : PageRequest
{
    /// <summary>The kind of record, such as "Customer".</summary>
    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public Guid? UserId { get; init; }

    /// <summary>An <see cref="AuditAction"/> name, such as "SignInFailed".</summary>
    public string? Action { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}
