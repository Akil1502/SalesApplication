using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using SalesApp.Infrastructure.Security;

namespace SalesApp.Api.Security;

/// <summary>
/// Requires the signed-in user to hold at least one of the given permissions (keys from
/// <see cref="Core.Security.Permissions"/>). Several attributes on one action must all pass.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(params string[] anyOf) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PermissionRequirement(AnyOf)];
}

public sealed record PermissionRequirement(IReadOnlyList<string> AnyOf) : IAuthorizationRequirement;

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (requirement.AnyOf.Any(p => context.User.HasClaim(AppClaimTypes.Permission, p)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds the user's current role and permissions to the request's identity, read from the database (cached briefly)
/// rather than from the token, so an administrator's change applies on the user's next request. A deactivated user
/// gets no permissions at all.
/// </summary>
public sealed class PermissionClaimsTransformation(IPermissionService permissions) : IClaimsTransformation
{
    private const string AccessIdentity = "SalesApp.Access";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // It can run more than once per request; the access identity marks that it already has.
        if (principal.Identity?.IsAuthenticated != true || principal.Identities.Any(i => i.AuthenticationType == AccessIdentity)
            || !Guid.TryParse(principal.FindFirst(AppClaimTypes.UserId)?.Value, out var userId))
        {
            return principal;
        }

        var access = await permissions.GetAsync(userId, CancellationToken.None);
        var identity = new ClaimsIdentity(AccessIdentity);
        if (access is not null)
        {
            identity.AddClaim(new Claim(AppClaimTypes.RoleName, access.Role));
            identity.AddClaims(access.Permissions.Select(p => new Claim(AppClaimTypes.Permission, p)));
        }

        var transformed = principal.Clone();
        transformed.AddIdentity(identity);
        return transformed;
    }
}
