using SalesApp.Core.Security;

namespace SalesApp.Api.Security;

/// <summary>
/// <see cref="ICurrentUser"/> for the request's signed-in user: the id from the token, the role and permissions as
/// read from the database for this request (see <see cref="PermissionClaimsTransformation"/>).
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(AppClaimTypes.UserId)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("No authenticated user on the current request.");

    public bool Has(string permission) => accessor.HttpContext?.User.HasClaim(AppClaimTypes.Permission, permission) ?? false;

    public string RoleName => accessor.HttpContext?.User.FindFirst(AppClaimTypes.RoleName)?.Value ?? string.Empty;
}
