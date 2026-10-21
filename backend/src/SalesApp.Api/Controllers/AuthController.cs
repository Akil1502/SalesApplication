using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Auth;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Security;

namespace SalesApp.Api.Controllers;

public sealed class AuthController(
    UserManager<AppUser> userManager,
    IJwtTokenService tokenService,
    IPermissionService permissions,
    IAuditLog audit,
    ILogger<AuthController> logger) : ApiControllerBase
{
    // Same message for unknown email, wrong password, locked out and deactivated users,
    // so the login form can't be used to discover which accounts exist.
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    /// <summary>Exchange email and password for an access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            await FailedAsync(user, request.Email, user is null ? L.Msg("No account with this email.") : L.Msg("The account is deactivated."));
            return InvalidCredentials();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Login attempt for locked-out user {UserId}", user.Id);
            await FailedAsync(user, request.Email, L.Msg("The account is locked after too many wrong passwords."));
            return InvalidCredentials();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Counts towards lockout (5 failures => 15 minutes, configured in AddInfrastructure).
            await userManager.AccessFailedAsync(user);
            await FailedAsync(user, request.Email, L.Msg("Wrong password."));
            if (await userManager.IsLockedOutAsync(user))
            {
                await audit.RecordAsync(new AuditEvent(AuditAction.LockedOut, L.Msg("User"), user.Id.ToString(), Label(user),
                    L.Msg("Locked for 15 minutes after too many wrong passwords."), UserId: user.Id, UserName: user.FullName), HttpContext.RequestAborted);
            }

            return InvalidCredentials();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        await audit.RecordAsync(new AuditEvent(AuditAction.SignedIn, L.Msg("User"), user.Id.ToString(), Label(user), L.Msg("Signed in."),
            UserId: user.Id, UserName: user.FullName), HttpContext.RequestAborted);

        var (token, expiresAtUtc) = tokenService.CreateToken(user);
        return new LoginResponse(token, expiresAtUtc, await ToDtoAsync(user));
    }

    /// <summary>The signed-in user, read fresh from the database so role and permission changes show up at once.</summary>
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

        return await ToDtoAsync(user);
    }

    private string TraceId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private static string Label(AppUser user) => $"{user.FullName} ({user.Email})";

    /// <summary>A failed sign-in, recorded under the email that was tried (the account may not exist).</summary>
    private Task FailedAsync(AppUser? user, string email, string why) =>
        audit.RecordAsync(new AuditEvent(AuditAction.SignInFailed, user is null ? null : L.Msg("User"), user?.Id.ToString(), user is null ? null : Label(user),
            why, UserId: user?.Id, UserName: email.Trim()), HttpContext.RequestAborted);

    private UnauthorizedObjectResult InvalidCredentials() => Unauthorized(new ApiError(InvalidCredentialsMessage, TraceId));

    private async Task<CurrentUserDto> ToDtoAsync(AppUser user)
    {
        var access = await permissions.GetAsync(user.Id, HttpContext.RequestAborted);
        return new CurrentUserDto(user.Id, user.Email ?? string.Empty, user.FullName, access?.Role ?? string.Empty,
            [.. (access?.Permissions ?? new HashSet<string>()).Order(StringComparer.Ordinal)]);
    }
}
