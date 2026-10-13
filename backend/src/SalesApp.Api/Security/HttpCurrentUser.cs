using SalesApp.Core.Security;

namespace SalesApp.Api.Security;

/// <summary><see cref="ICurrentUser"/> backed by the authenticated request's JWT claims.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(AppClaimTypes.UserId)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("No authenticated user on the current request.");

    public bool IsInRole(string role) => accessor.HttpContext?.User.IsInRole(role) ?? false;
}
