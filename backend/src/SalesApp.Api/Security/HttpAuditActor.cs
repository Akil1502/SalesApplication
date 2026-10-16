using SalesApp.Core.Security;

namespace SalesApp.Api.Security;

/// <summary>
/// The audit actor for a request: the signed-in user (id and name from their token) and the address the request came
/// from. Background work that set <see cref="CurrentUserOverride"/>, and anything without a request, is "System".
/// </summary>
public sealed class HttpAuditActor(IHttpContextAccessor accessor, CurrentUserOverride currentUserOverride) : IAuditActor
{
    private HttpContext? Context => currentUserOverride.User is null ? accessor.HttpContext : null;

    public Guid? UserId =>
        Guid.TryParse(Context?.User.FindFirst(AppClaimTypes.UserId)?.Value, out var id) ? id : null;

    public string Name => Context switch
    {
        null => "System",
        { User.Identity.IsAuthenticated: true } context => context.User.FindFirst(AppClaimTypes.Name)?.Value ?? "Unknown user",
        _ => "Not signed in",
    };

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();
}
