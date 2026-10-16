using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Products. Everyone can browse; only managers and admins maintain the catalogue (and see cost prices).</summary>
public sealed class ProductsController(IProductService products, IProductImageService images, IProductImageUrls imageUrls) : ApiControllerBase
{
    private const long MaxPhotoUploadBytes = ProductImageService.MaxImageBytes + ProductImageService.MaxThumbnailBytes + 64 * 1024;

    /// <summary>Search products by SKU, name or HSN code.</summary>
    [HttpGet]
    [RequirePermission(Permissions.ProductsView, Permissions.OrdersEdit, Permissions.StockEdit)]
    [ProducesResponseType<PagedResult<ProductListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ProductListItemDto>> List([FromQuery] ProductListRequest request, CancellationToken cancellationToken) =>
        products.ListAsync(request, cancellationToken);

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.ProductsView, Permissions.OrdersEdit, Permissions.StockEdit)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ProductDto> Get(int id, CancellationToken cancellationToken) => products.GetAsync(id, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.ProductsEdit)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductDto>> Create(ProductSaveRequest request, CancellationToken cancellationToken)
    {
        var product = await products.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.ProductsEdit)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<ProductDto> Update(int id, ProductSaveRequest request, CancellationToken cancellationToken) =>
        products.UpdateAsync(id, request, cancellationToken);

    /// <summary>
    /// Every product photo, so any screen can show a picture next to a product. Open to everyone signed in: photos
    /// show on orders, invoices and stock screens as well as the product list.
    /// </summary>
    [HttpGet("images")]
    [ProducesResponseType<IReadOnlyList<ProductImageDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ProductImageDto>> Images(CancellationToken cancellationToken) => images.ListAsync(cancellationToken);

    /// <summary>
    /// Sets the product's photo. The browser sends it in two sizes it has already shrunk: the picture (at most
    /// 1.5 MB) and a thumbnail (at most 192 KB).
    /// </summary>
    [HttpPut("{id:int}/image")]
    [RequirePermission(Permissions.ProductsEdit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxPhotoUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPhotoUploadBytes)]
    [ProducesResponseType<ProductImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ProductImageDto> SetImage(int id, [FromForm] ProductImageForm form, CancellationToken cancellationToken) =>
        await images.SaveAsync(
            id,
            await ReadAsync("image", form.Image, ProductImageService.MaxImageBytes, cancellationToken),
            await ReadAsync("thumbnail", form.Thumbnail, ProductImageService.MaxThumbnailBytes, cancellationToken),
            cancellationToken);

    [HttpDelete("{id:int}/image")]
    [RequirePermission(Permissions.ProductsEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(int id, CancellationToken cancellationToken)
    {
        await images.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// The picture itself. Anonymous because an &lt;img&gt; tag can't send the sign-in token: the signed address
    /// (from <see cref="Images"/> or a product) is the permission. Each address names one version of the photo, so
    /// browsers may keep it for a year.
    /// </summary>
    [HttpGet("{id:int}/image/{size}")]
    [AllowAnonymous]
    [Produces("image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Image(int id, ProductImageSize size, [FromQuery] int v, [FromQuery] string? s, CancellationToken cancellationToken)
    {
        if (!imageUrls.IsValid(id, v, size, s) || await images.ReadAsync(id, v, size, cancellationToken) is not { } image)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(image.Data, image.ContentType);
    }

    private static async Task<byte[]> ReadAsync(string field, IFormFile? upload, int maxBytes, CancellationToken cancellationToken)
    {
        if (upload is not { Length: > 0 })
        {
            throw new ValidationException(field, "Choose a picture.");
        }

        if (upload.Length > maxBytes)
        {
            throw new ValidationException(field, string.Create(CultureInfo.InvariantCulture, $"The picture is too large; it can be at most {maxBytes / 1024} KB."));
        }

        using var buffer = new MemoryStream((int)upload.Length);
        await upload.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}

public sealed record ProductImageForm
{
    public IFormFile? Image { get; init; }
    public IFormFile? Thumbnail { get; init; }
}
