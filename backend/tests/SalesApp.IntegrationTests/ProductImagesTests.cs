using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.IntegrationTests;

/// <summary>Product photos: uploads checked by content, signed picture addresses, versions, and the audit trail.</summary>
public sealed class ProductImagesTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Jpeg(int size = 2000) => [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Range(0, size).Select(i => (byte)(i % 251))];

    private static byte[] Png(int size = 300) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Range(0, size).Select(i => (byte)(i % 13))];

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, int productId, byte[] image, byte[] thumbnail)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(image) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "image", "photo.jpg" },
            { new ByteArrayContent(thumbnail) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "thumbnail", "thumb.png" },
        };
        return client.PutAsync($"/api/products/{productId}/image", form, Ct);
    }

    [Fact]
    public async Task A_photo_is_served_from_its_signed_address_without_signing_in()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin);
        var image = Jpeg();
        var thumbnail = Png();

        var response = await UploadAsync(admin, product.Id, image, thumbnail);
        response.EnsureSuccessStatusCode();
        var saved = await response.ReadAsync<ProductImageDto>();
        Assert.Equal(1, saved.Version);

        var anonymous = factory.CreateClient();
        var full = await anonymous.GetAsync(saved.ImageUrl, Ct);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal("image/jpeg", full.Content.Headers.ContentType?.MediaType);
        Assert.Equal(image, await full.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("immutable", full.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        var thumb = await anonymous.GetAsync(saved.ThumbnailUrl, Ct);
        Assert.Equal("image/png", thumb.Content.Headers.ContentType?.MediaType);
        Assert.Equal(thumbnail, await thumb.Content.ReadAsByteArrayAsync(Ct));

        // The product and the photo list both point at the same addresses.
        Assert.Equal(saved, (await (await admin.GetAsync($"/api/products/{product.Id}", Ct)).ReadAsync<ProductDto>()).Image);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        Assert.Contains(saved, await (await rep.GetAsync("/api/products/images", Ct)).ReadAsync<List<ProductImageDto>>());
    }

    [Fact]
    public async Task A_changed_or_guessed_address_finds_nothing()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin);
        var other = await TestData.CreateProductAsync(admin);
        var saved = await (await UploadAsync(admin, product.Id, Jpeg(), Png())).ReadAsync<ProductImageDto>();
        await UploadAsync(admin, other.Id, Jpeg(), Png());
        var anonymous = factory.CreateClient();

        var signature = saved.ImageUrl[(saved.ImageUrl.IndexOf("&s=", StringComparison.Ordinal) + 3)..];
        string[] tampered =
        [
            saved.ImageUrl.Replace($"/products/{product.Id}/", $"/products/{other.Id}/", StringComparison.Ordinal),
            saved.ImageUrl.Replace("/image/full", "/image/thumb", StringComparison.Ordinal),
            saved.ImageUrl.Replace("&s=" + signature, "&s=" + new string('A', signature.Length), StringComparison.Ordinal),
            saved.ImageUrl[..saved.ImageUrl.IndexOf("&s=", StringComparison.Ordinal)],
        ];
        foreach (var url in tampered)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task A_new_photo_gets_a_new_address_and_removing_it_leaves_none()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin);
        var first = await (await UploadAsync(admin, product.Id, Jpeg(1000), Png())).ReadAsync<ProductImageDto>();
        var second = await (await UploadAsync(admin, product.Id, Jpeg(3000), Png())).ReadAsync<ProductImageDto>();
        var anonymous = factory.CreateClient();

        Assert.Equal(2, second.Version);
        Assert.NotEqual(first.ImageUrl, second.ImageUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.ImageUrl, Ct)).StatusCode);
        Assert.Equal(3004, (await (await anonymous.GetAsync(second.ImageUrl, Ct)).Content.ReadAsByteArrayAsync(Ct)).Length);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}/image", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(second.ImageUrl, Ct)).StatusCode);
        Assert.Null((await (await admin.GetAsync($"/api/products/{product.Id}", Ct)).ReadAsync<ProductDto>()).Image);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/products/{product.Id}/image", Ct)).StatusCode);

        // Each change shows in the product's history; the picture itself never goes into the trail.
        var history = (await (await admin.GetAsync($"/api/audit?pageSize=10&entityType=Product&entityId={product.Id}", Ct)).ReadAsync<PagedResult<AuditEntryDto>>()).Items;
        Assert.Equal(
            [new AuditChange("Photo", "Photo", "Removed"), new AuditChange("Photo", "Earlier photo", "New photo"), new AuditChange("Photo", null, "Added")],
            history.Where(h => h.Changes.Any(c => c.Field == "Photo")).Select(h => Assert.Single(h.Changes)));
    }

    [Fact]
    public async Task A_photo_added_after_one_was_removed_never_reuses_its_address()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin);
        var anonymous = factory.CreateClient();
        var first = await (await UploadAsync(admin, product.Id, Jpeg(1000), Png())).ReadAsync<ProductImageDto>();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}/image", Ct)).StatusCode);
        Assert.DoesNotContain(await (await admin.GetAsync("/api/products/images", Ct)).ReadAsync<List<ProductImageDto>>(), i => i.ProductId == product.Id);

        // The removal took a version of its own, and there's no picture behind it.
        var removed = factory.Services.GetRequiredService<IProductImageUrls>().Create(product.Id, first.Version + 1);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(removed.ImageUrl, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(removed.ThumbnailUrl, Ct)).StatusCode);

        // Browsers keep a picture for a year, so the new photo must not come back at the first one's address.
        var again = await (await UploadAsync(admin, product.Id, Jpeg(3000), Png())).ReadAsync<ProductImageDto>();
        Assert.NotEqual(first.ImageUrl, again.ImageUrl);
        Assert.NotEqual(first.ThumbnailUrl, again.ThumbnailUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.ImageUrl, Ct)).StatusCode);
        Assert.Equal(3004, (await (await anonymous.GetAsync(again.ImageUrl, Ct)).Content.ReadAsByteArrayAsync(Ct)).Length);
        Assert.Equal(again, (await (await admin.GetAsync($"/api/products/{product.Id}", Ct)).ReadAsync<ProductDto>()).Image);
        Assert.Contains(again, await (await admin.GetAsync("/api/products/images", Ct)).ReadAsync<List<ProductImageDto>>());

        // A photo added after a removal shows as added, not as replacing the removed one.
        var history = (await (await admin.GetAsync($"/api/audit?pageSize=10&entityType=Product&entityId={product.Id}", Ct)).ReadAsync<PagedResult<AuditEntryDto>>()).Items;
        Assert.Equal(
            [new AuditChange("Photo", null, "Added"), new AuditChange("Photo", "Photo", "Removed"), new AuditChange("Photo", null, "Added")],
            history.Where(h => h.Changes.Any(c => c.Field == "Photo")).Select(h => Assert.Single(h.Changes)));
    }

    [Fact]
    public async Task Only_pictures_are_accepted_and_only_from_people_who_edit_products()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var product = await TestData.CreateProductAsync(admin);

        var pdf = await UploadAsync(admin, product.Id, "%PDF-1.7 not a picture"u8.ToArray(), Png());
        Assert.Equal(HttpStatusCode.BadRequest, pdf.StatusCode);
        Assert.Contains("image", (await pdf.ReadAsync<ApiError>()).Errors!.Keys);

        var hugeThumbnail = await UploadAsync(admin, product.Id, Jpeg(), Png(200 * 1024));
        Assert.Equal(HttpStatusCode.BadRequest, hugeThumbnail.StatusCode);
        Assert.Contains("thumbnail", (await hugeThumbnail.ReadAsync<ApiError>()).Errors!.Keys);

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(rep, product.Id, Jpeg(), Png())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(admin, int.MaxValue, Jpeg(), Png())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/products/images", Ct)).StatusCode);
    }
}
