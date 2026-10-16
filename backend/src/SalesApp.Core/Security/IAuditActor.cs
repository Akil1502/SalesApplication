namespace SalesApp.Core.Security;

/// <summary>Who an audit entry is attributed to, and from where: the signed-in user, or "System" for background work.</summary>
public interface IAuditActor
{
    Guid? UserId { get; }

    string Name { get; }

    string? IpAddress { get; }
}

/// <summary>The actor for work with no signed-in user: startup, background jobs, command-line tools.</summary>
public sealed class SystemAuditActor : IAuditActor
{
    public Guid? UserId => null;

    public string Name => "System";

    public string? IpAddress => null;
}
