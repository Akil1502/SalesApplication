using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Auth;
using SalesApp.Core.Entities;

namespace SalesApp.Api.Controllers;

public sealed class AuthController(
    UserManager<AppUser> userManager,
    IJwtTokenService tokenService,
    ILogger<AuthController> logger) : ApiControllerBase
{
    // Same message for unknown email, wrong password, locked out and deactivated users,
    // so the login form can't be used to discover which accounts exist.
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    /// <summary>Exchange email and password for an access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            return InvalidCredentials();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Login attempt for locked-out user {UserId}", user.Id);
            return InvalidCredentials();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Counts towards lockout (5 failures => 15 minutes, configured in AddInfrastructure).
            await userManager.AccessFailedAsync(user);
            return InvalidCredentials();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAtUtc) = tokenService.CreateToken(user, roles);
        return new LoginResponse(token, expiresAtUtc, ToDto(user, roles));
    }

    /// <summary>The signed-in user, read fresh from the database so role changes show up immediately.</summary>
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Me()
    {
        var user = await userManager.FindByIdAsync(CurrentUserId.ToString());
        if (user is null || !user.IsActive)
        {
            // 401 (not 404) so the React app treats it like an expired session and signs out.
            return Unauthorized(new ApiError("Your account is no longer active.", TraceId));
        }

        return ToDto(user, await userManager.GetRolesAsync(user));
    }

    private string TraceId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private UnauthorizedObjectResult InvalidCredentials() => Unauthorized(new ApiError(InvalidCredentialsMessage, TraceId));

    private static CurrentUserDto ToDto(AppUser user, IList<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FullName, [.. roles]);
}
