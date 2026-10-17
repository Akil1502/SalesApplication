using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

/// <summary>The company logo: open before sign-in, versioned addresses, checked uploads and the audit trail.</summary>
public sealed class BrandingTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Png(int size = 500) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Range(0, size).Select(i => (byte)(i % 17))];

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] logo)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(logo) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "logo", "logo.png" },
        };
        return client.PutAsync("/api/settings/company/logo", form, Ct);
    }

    private static int VersionOf(string url) => int.Parse(url[(url.IndexOf("?v=", StringComparison.Ordinal) + 3)..], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_logo_is_open_before_sign_in_and_every_change_gets_a_new_address()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var anonymous = factory.CreateClient();

        var first = await (await UploadAsync(admin, Png(400))).ReadAsync<BrandingDto>();
        Assert.NotNull(first.LogoUrl);
        var logo = await anonymous.GetAsync(first.LogoUrl, Ct);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png(400), await logo.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("immutable", logo.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(first.LogoUrl, (await (await anonymous.GetAsync("/api/branding", Ct)).ReadAsync<BrandingDto>()).LogoUrl);
        Assert.Equal(first.LogoUrl, (await (await admin.GetAsync("/api/settings/company", Ct)).ReadAsync<CompanySettingsDto>()).LogoUrl);

        var second = await (await UploadAsync(admin, Png(900))).ReadAsync<BrandingDto>();
        Assert.True(VersionOf(second.LogoUrl!) > VersionOf(first.LogoUrl));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.LogoUrl, Ct)).StatusCode);

        var removed = await (await admin.DeleteAsync("/api/settings/company/logo", Ct)).ReadAsync<BrandingDto>();
        Assert.Null(removed.LogoUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(second.LogoUrl, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/settings/company/logo", Ct)).StatusCode);

        // A logo added after one was removed never reuses an address a browser may have cached.
        var third = await (await UploadAsync(admin, Png(400))).ReadAsync<BrandingDto>();
        Assert.True(VersionOf(third.LogoUrl!) > VersionOf(second.LogoUrl!));

        var history = (await (await admin.GetAsync("/api/audit?pageSize=20&entityType=Company%20settings&entityId=1", Ct)).ReadAsync<PagedResult<AuditEntryDto>>()).Items;
        Assert.Equal(
            [new AuditChange("Logo", null, "Added"), new AuditChange("Logo", "Logo", "Removed"), new AuditChange("Logo", "Earlier logo", "New logo")],
            history.Where(h => h.Changes.Any(c => c.Field == "Logo")).Select(h => Assert.Single(h.Changes)).Take(3));

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/settings/company/logo", Ct)).StatusCode);
    }

    [Fact]
    public async Task Before_sign_in_only_the_name_colour_and_logo_are_shown()
    {
        var response = await factory.CreateClient().GetAsync("/api/branding", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        // Empty values are left out of responses, so check nothing beyond these three ever appears.
        var fields = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("companyName", fields);
        Assert.Empty(fields.Except(["companyName", "brandColour", "logoUrl"]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/settings/company", Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_pictures_are_accepted_and_only_from_people_who_change_settings()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var svg = await UploadAsync(admin, "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, svg.StatusCode);
        Assert.Contains("logo", (await svg.ReadAsync<ApiError>()).Errors!.Keys);

        var huge = await UploadAsync(admin, Png(520 * 1024));
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
        Assert.Contains("logo", (await huge.ReadAsync<ApiError>()).Errors!.Keys);

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(manager, Png())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync("/api/settings/company/logo", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UploadAsync(factory.CreateClient(), Png())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/api/branding/logo?v=999999", Ct)).StatusCode);
    }
}
