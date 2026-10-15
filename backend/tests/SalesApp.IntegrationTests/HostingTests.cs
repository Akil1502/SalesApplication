using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using SalesApp.Core.Contracts.Auth;

namespace SalesApp.IntegrationTests;

/// <summary>How the app is served: security headers, the React app's pages and files, and sign-in limits.</summary>
public sealed class HostingTests(SalesAppFactory factory) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _webRoot = Directory.CreateTempSubdirectory("salesapp-webroot-").FullName;

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        var response = await factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self';", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_of_the_web_app_get_index_html_and_unknown_api_paths_stay_404()
    {
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), "<!doctype html><div id=\"root\"></div>", Ct);
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "index-abc123.js"), "console.log(1)", Ct);
        var client = factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot)).CreateClient();

        var page = await client.GetAsync("/customers/42", Ct); // no sign-in needed to load the app
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"root\"", await page.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(page.Headers.CacheControl?.NoCache);

        var asset = await client.GetAsync("/assets/index-abc123.js", Ct);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(TimeSpan.FromDays(365), asset.Headers.CacheControl?.MaxAge);

        var api = await client.GetAsync("/api/no-such-thing", Ct);
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Equal("application/json", api.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Without_a_built_web_app_pages_explain_how_to_build_it()
    {
        var client = factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot)).CreateClient();

        var page = await client.GetAsync("/insights", Ct);

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Contains("npm run build --prefix frontend", await page.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_sign_in_attempts_are_slowed_down()
    {
        var client = factory.WithWebHostBuilder(builder => builder.UseSetting("Security:LoginAttemptsPerMinute", "2")).CreateClient();
        var wrong = new LoginRequest { Email = "nobody@salesapp.test", Password = "Wrong-Password-1" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", wrong, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", wrong, Ct)).StatusCode);
        var third = await client.PostAsJsonAsync("/api/auth/login", wrong, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("Too many sign-in attempts", await third.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
