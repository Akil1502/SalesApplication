using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Contracts.Auth;
using SalesApp.Core.Entities;

namespace SalesApp.IntegrationTests;

/// <summary>Helpers for signing in as users of a given role and reading API responses.</summary>
public static class TestClients
{
    public const string DefaultPassword = "Test-Password-123";

    /// <summary>Matches the API's JSON settings (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Creates a brand-new user in <paramref name="role"/> and returns a client carrying their token.</summary>
    public static async Task<(HttpClient Client, Guid UserId)> CreateClientAsync(this WebApplicationFactory<Program> factory, string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@salesapp.test";
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { UserName = email, Email = email, FullName = $"Test {role}", CreatedAtUtc = DateTime.UtcNow };
            var created = await users.CreateAsync(user, DefaultPassword);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(user, role);
            userId = user.Id;
        }

        return (await factory.SignInAsync(email, DefaultPassword), userId);
    }

    public static async Task<HttpClient> SignInAsync(this WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Json);
        Assert.NotNull(value);
        return value;
    }
}
