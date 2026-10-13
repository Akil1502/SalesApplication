using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Security;

namespace SalesApp.Api.Controllers;

/// <summary>
/// Base for all API controllers: JSON in/out, /api/[controller] routes, authenticated by default.
/// Endpoints that must be anonymous (login) opt out with [AllowAnonymous].
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The signed-in user's id from the "sub" claim.</summary>
    protected Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(AppClaimTypes.UserId)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated request has no valid 'sub' claim.");
}
