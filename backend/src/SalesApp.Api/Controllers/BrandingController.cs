using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>
/// The company's name, colour and logo, open to everyone because the sign-in page shows them before anyone has
/// signed in. Nothing else from the company settings is exposed here.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Public)]
public sealed class BrandingController(ICompanySettingsService settings) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<BrandingDto>(StatusCodes.Status200OK)]
    public Task<BrandingDto> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        return settings.GetBrandingAsync(cancellationToken);
    }

    /// <summary>The logo. Each address names one version of it, so browsers may keep it for a year.</summary>
    [HttpGet("logo")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Logo([FromQuery] int v, CancellationToken cancellationToken)
    {
        if (await settings.ReadLogoAsync(v, cancellationToken) is not { } logo)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(logo.Data, logo.ContentType);
    }
}
