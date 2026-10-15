namespace SalesApp.Api.Security;

/// <summary>
/// Browser security headers on every response. The content security policy fits the built React app: scripts,
/// styles, fonts and API calls come only from this site; inline styles are allowed (the charts set them), images
/// may also be data: or blob: URLs (upload previews), and the app can't be framed by another site.
/// </summary>
public static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <param name="app">The pipeline.</param>
    /// <param name="contentSecurityPolicy">Off in development, where the API explorer (Scalar) needs inline scripts.</param>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool contentSecurityPolicy) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                if (contentSecurityPolicy)
                {
                    headers["Content-Security-Policy"] = ContentSecurityPolicy;
                }

                return Task.CompletedTask;
            });
            await next();
        });
}
