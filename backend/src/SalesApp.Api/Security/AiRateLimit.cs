using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SalesApp.AI;
using SalesApp.Api.Errors;

namespace SalesApp.Api.Security;

/// <summary>
/// Caps how often each user can call AI endpoints (a fixed number per minute), on top of the gateway's daily
/// spending budget. Stops a stuck button or script from running up the bill.
/// </summary>
public static class AiRateLimit
{
    public const string PolicyName = "ai";

    public static IServiceCollection AddAiRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var perMinute = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>()?.RequestsPerMinutePerUser ?? 10;

        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(AppClaimTypes.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(new ApiError(
                    "You're sending AI requests too quickly. Wait a minute and try again.",
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier), cancellationToken);
            };
        });
    }
}
