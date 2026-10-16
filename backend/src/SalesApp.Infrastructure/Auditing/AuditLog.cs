using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Infrastructure.Auditing;

/// <summary>
/// An audit event that isn't a field change: a sign-in, an export, a permission change. UserId and UserName replace
/// the current user for events about someone not signed in yet (a sign-in attempt).
/// </summary>
public sealed record AuditEvent(
    AuditAction Action,
    string? EntityType = null,
    string? EntityId = null,
    string? EntityLabel = null,
    string? Summary = null,
    IReadOnlyList<AuditChange>? Changes = null,
    Guid? UserId = null,
    string? UserName = null);

public interface IAuditLog
{
    /// <summary>Records an event. It is saved straight away, so call it after the save it belongs with.</summary>
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    Task<PagedResult<AuditEntryDto>> ListAsync(AuditListRequest request, CancellationToken cancellationToken);
}

public sealed class AuditLog(AppDbContext db, IAuditActor actor, TimeProvider time, IBusinessClock clock) : IAuditLog
{
    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAtUtc = time.GetUtcNow().UtcDateTime,
            UserId = auditEvent.UserName is null ? actor.UserId : auditEvent.UserId,
            UserName = Clip(auditEvent.UserName ?? actor.Name, 256)!,
            IpAddress = actor.IpAddress,
            Action = auditEvent.Action,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            EntityLabel = Clip(auditEvent.EntityLabel, 300),
            Summary = Clip(auditEvent.Summary, 1000),
            ChangesJson = auditEvent.Changes is { Count: > 0 } changes ? JsonSerializer.Serialize(changes, AuditInterceptor.Json) : null,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditEntryDto>> ListAsync(AuditListRequest request, CancellationToken cancellationToken)
    {
        var query = db.AuditEntries.AsNoTracking();
        if (Text.NullIfBlank(request.EntityType) is { } type)
        {
            query = query.Where(a => a.EntityType == type);
        }

        if (Text.NullIfBlank(request.EntityId) is { } id)
        {
            query = query.Where(a => a.EntityId == id);
        }

        if (request.UserId is { } userId)
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (Text.NullIfBlank(request.Action) is { } actionText)
        {
            if (!Enum.TryParse<AuditAction>(actionText, ignoreCase: true, out var action))
            {
                throw new ValidationException("action", $"'{actionText}' isn't an audit action.");
            }

            query = query.Where(a => a.Action == action);
        }

        // Days are India days, as people mean them.
        if (request.From is { } from)
        {
            var start = StartUtc(from);
            query = query.Where(a => a.OccurredAtUtc >= start);
        }

        if (request.To is { } to)
        {
            var end = StartUtc(to.AddDays(1));
            query = query.Where(a => a.OccurredAtUtc < end);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(a => (a.EntityLabel != null && a.EntityLabel.Contains(q)) || (a.Summary != null && a.Summary.Contains(q))
                || a.UserName.Contains(q) || (a.ChangesJson != null && a.ChangesJson.Contains(q)));
        }

        var page = await query.OrderByDescending(a => a.Id).ToPagedResultAsync(request, cancellationToken);
        return new PagedResult<AuditEntryDto>(
            [.. page.Items.Select(a => new AuditEntryDto(
                a.Id, a.OccurredAtUtc, a.UserId, a.UserName, a.Action, a.EntityType, a.EntityId, a.EntityLabel, a.Summary,
                a.ChangesJson is null ? [] : JsonSerializer.Deserialize<List<AuditChange>>(a.ChangesJson, AuditInterceptor.Json) ?? [],
                a.IpAddress))],
            page.Page, page.PageSize, page.TotalCount);
    }

    private DateTime StartUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.TimeZone);

    private static string? Clip(string? text, int max) => text is null ? null : text.Length > max ? text[..(max - 1)] + "…" : text;
}
