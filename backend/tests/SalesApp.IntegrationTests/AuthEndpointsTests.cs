using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Auth;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

public sealed class AuthEndpointsTests(SalesAppFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_endpoint_is_public_and_reports_the_database_healthy()
    {
        var response = await _client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Protected_endpoint_without_a_token_returns_401_with_an_ApiError()
    {
        var response = await _client.GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(Ct);
        Assert.False(string.IsNullOrEmpty(error?.TraceId));
    }

    [Fact]
    public async Task Seeded_admin_can_sign_in_and_read_their_profile()
    {
        var login = await LoginAsync(SalesAppFactory.AdminEmail, SalesAppFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(Ct);
        Assert.NotNull(body);
        Assert.Equal(Roles.Admin, body.User.Role);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), body.User.Permissions);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        var me = await _client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(SalesAppFactory.AdminEmail, (await me.Content.ReadFromJsonAsync<CurrentUserDto>(Ct))?.Email);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_message()
    {
        var wrongPassword = await LoginAsync(SalesAppFactory.AdminEmail, "not-the-password");
        var unknownEmail = await LoginAsync("nobody@salesapp.test", "whatever-123");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            (await wrongPassword.Content.ReadFromJsonAsync<ApiError>(Ct))?.Message,
            (await unknownEmail.Content.ReadFromJsonAsync<ApiError>(Ct))?.Message);
    }

    [Fact]
    public async Task Invalid_login_body_returns_field_level_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "not-an-email" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(Ct);
        Assert.NotNull(error?.Errors);
        Assert.Contains("email", error.Errors.Keys);
        Assert.Contains("password", error.Errors.Keys);
    }

    [Fact]
    public async Task Account_locks_after_five_failed_attempts()
    {
        var email = await CreateUserAsync("lockout", Roles.SalesRep, "Correct-Password-1");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LoginAsync(email, "wrong-password");
        }

        var withCorrectPassword = await LoginAsync(email, "Correct-Password-1");
        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in_and_their_existing_token_stops_working()
    {
        var email = await CreateUserAsync("deactivated", Roles.SalesRep, "Correct-Password-1");
        var token = (await (await LoginAsync(email, "Correct-Password-1")).Content.ReadFromJsonAsync<LoginResponse>(Ct))!.AccessToken;

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            user!.IsActive = false;
            await users.UpdateAsync(user);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "Correct-Password-1")).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request, Ct)).StatusCode);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password }, Ct);

    private async Task<string> CreateUserAsync(string prefix, string role, string password)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@salesapp.test";
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, FullName = $"Test {prefix}", CreatedAtUtc = DateTime.UtcNow };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return email;
    }
}
