using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SalesApp.AI;
using SalesApp.Api.Errors;

namespace SalesApp.Api.Security;

/// <summary>
/// Request limits on top of the business rules:
/// <list type="bullet">
/// <item><see cref="Ai"/>: each user may call AI endpoints a fixed number of times a minute, on top of the gateway's
/// daily spending budget, so a stuck button or script can't run up the bill.</item>
/// <item><see cref="Login"/>: sign-in attempts per client address per minute. Account lockout stops guessing one
/// account's password; this also slows trying one password against many accounts.</item>
/// </list>
/// </summary>
public static class RateLimits
{
    public const string Ai = "ai";
    public const string Login = "login";

    public static IServiceCollection AddAppRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var aiPerMinute = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>()?.RequestsPerMinutePerUser ?? 10;
        var loginsPerMinute = configuration.GetValue("Security:LoginAttemptsPerMinute", 10);

        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Ai, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(AppClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = aiPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.AddPolicy(Login, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = loginsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(new ApiError(
                    policy == Login
                        ? "Too many sign-in attempts. Wait a minute and try again."
                        : "You're sending AI requests too quickly. Wait a minute and try again.",
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier), cancellationToken);
            };
        });
    }
}
