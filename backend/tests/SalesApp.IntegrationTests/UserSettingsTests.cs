using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SalesApp.Api.Errors;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

/// <summary>Each person's own settings: saved, replaced, removed, private to them, and capped in size.</summary>
public sealed class UserSettingsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Dictionary<string, JsonElement>> SettingsAsync(HttpClient client) =>
        await (await client.GetAsync("/api/me/settings", Ct)).ReadAsync<Dictionary<string, JsonElement>>();

    [Fact]
    public async Task A_setting_is_saved_replaced_and_removed()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var saved = await rep.PutAsJsonAsync("/api/me/settings/views.invoices", new[] { new { name = "Overdue, Tiruppur", search = "?view=overdue&q=Tiruppur" } }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var view = Assert.Single((await SettingsAsync(rep))["views.invoices"].EnumerateArray());
        Assert.Equal("Overdue, Tiruppur", view.GetProperty("name").GetString());

        await rep.PutAsJsonAsync("/api/me/settings/views.invoices", Array.Empty<object>(), Ct);
        Assert.Equal(0, (await SettingsAsync(rep))["views.invoices"].GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await rep.DeleteAsync("/api/me/settings/views.invoices", Ct)).StatusCode);
        Assert.False((await SettingsAsync(rep)).ContainsKey("views.invoices"));
    }

    [Fact]
    public async Task Settings_are_private_to_each_person()
    {
        var (asha, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (ravi, _) = await factory.CreateClientAsync(Roles.SalesRep);

        await asha.PutAsJsonAsync("/api/me/settings/prefs", new { theme = "dark", layout = "dock" }, Ct);

        Assert.Equal("dark", (await SettingsAsync(asha))["prefs"].GetProperty("theme").GetString());
        Assert.False((await SettingsAsync(ravi)).ContainsKey("prefs"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/me/settings", Ct)).StatusCode);
    }

    [Fact]
    public async Task Bad_names_and_oversized_values_are_refused()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var badName = await rep.PutAsJsonAsync("/api/me/settings/Not%20Allowed", new { }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
        Assert.Contains("key", (await badName.ReadAsync<ApiError>()).Errors!.Keys);

        var huge = await rep.PutAsJsonAsync("/api/me/settings/recent", new { padding = new string('x', 40 * 1024) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
        Assert.Contains("value", (await huge.ReadAsync<ApiError>()).Errors!.Keys);
    }
}
