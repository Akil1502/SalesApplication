using System.Globalization;

namespace SalesApp.Api.Localization;

/// <summary>
/// Each request runs in the language the app is showing (its Accept-Language): Tamil or English. Only the UI culture
/// changes, so the server's messages and labels are translated (see <see cref="L"/>) while numbers, dates in files
/// and anything else formatted by culture stay exactly as before.
/// </summary>
public static class RequestLanguage
{
    public static IApplicationBuilder UseRequestLanguage(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            CultureInfo.CurrentUICulture = PrefersTamil(context.Request) ? L.Tamil : L.English;
            await next(context);
        });

    public static bool PrefersTamil(HttpRequest request)
    {
        var preferred = request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(language => language.Quality ?? 1)
            .FirstOrDefault();
        return preferred?.Value.Value?.StartsWith("ta", StringComparison.OrdinalIgnoreCase) == true;
    }
}
