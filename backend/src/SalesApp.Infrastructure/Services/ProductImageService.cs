using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public sealed class ProductImageOptions
{
    /// <summary>Signs picture addresses. Derived from the sign-in token key at start-up; never configured by hand.</summary>
    public byte[] SigningKey { get; set; } = [];
}

/// <summary>
/// Signed picture addresses. An &lt;img&gt; tag can't send the sign-in token, so instead the address carries a
/// signature over the product, photo version and size: only the server can make one, nobody can guess one for
/// another product, and a new photo gets a new address.
/// </summary>
public interface IProductImageUrls
{
    ProductImageDto Create(int productId, int version);
    bool IsValid(int productId, int version, ProductImageSize size, string? signature);
}

public sealed class ProductImageUrls(IOptions<ProductImageOptions> options) : IProductImageUrls
{
    public ProductImageDto Create(int productId, int version) =>
        new(productId, version, Url(productId, version, ProductImageSize.Full), Url(productId, version, ProductImageSize.Thumb));

    public bool IsValid(int productId, int version, ProductImageSize size, string? signature) =>
        signature is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(productId, version, size)), Encoding.ASCII.GetBytes(signature));

    private string Url(int productId, int version, ProductImageSize size) => string.Create(CultureInfo.InvariantCulture,
        $"/api/products/{productId}/image/{size.ToString().ToLowerInvariant()}?v={version}&s={Sign(productId, version, size)}");

    private string Sign(int productId, int version, ProductImageSize size)
    {
        if (options.Value.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("The product image signing key isn't configured.");
        }

        var message = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{productId}.{version}.{size}"));
        return Base64Url.EncodeToString(HMACSHA256.HashData(options.Value.SigningKey, message).AsSpan(0, 16));
    }
}

public sealed record ImageFile(byte[] Data, string ContentType);

public interface IProductImageService
{
    /// <summary>Every product that has a photo, for screens that show pictures next to products.</summary>
    Task<IReadOnlyList<ProductImageDto>> ListAsync(CancellationToken cancellationToken);

    Task<ProductImageDto> SaveAsync(int productId, byte[] image, byte[] thumbnail, CancellationToken cancellationToken);
    Task DeleteAsync(int productId, CancellationToken cancellationToken);

    /// <summary>The picture, or null when the product has no photo at that version (it has been replaced or removed).</summary>
    Task<ImageFile?> ReadAsync(int productId, int version, ProductImageSize size, CancellationToken cancellationToken);
}

public sealed class ProductImageService(AppDbContext db, IProductImageUrls urls, IAuditLog audit) : IProductImageService
{
    public const int MaxImageBytes = 1536 * 1024;
    public const int MaxThumbnailBytes = 192 * 1024;

    public async Task<IReadOnlyList<ProductImageDto>> ListAsync(CancellationToken cancellationToken)
    {
        var images = await db.ProductImages.AsNoTracking()
            .Where(i => i.ImageData != null)
            .OrderBy(i => i.ProductId)
            .Select(i => new { i.ProductId, i.Version })
            .ToListAsync(cancellationToken);
        return images.ConvertAll(i => urls.Create(i.ProductId, i.Version));
    }

    public async Task<ProductImageDto> SaveAsync(int productId, byte[] image, byte[] thumbnail, CancellationToken cancellationToken)
    {
        var imageType = Check("image", image, MaxImageBytes);
        var thumbnailType = Check("thumbnail", thumbnail, MaxThumbnailBytes);

        var version = await ChangeAsync(productId, hadPhoto => hadPhoto ? (L.Msg("Earlier photo"), L.Msg("New photo")) : (null, L.Msg("Added")), photo =>
        {
            photo.ContentType = imageType;
            photo.ImageData = image;
            photo.ThumbnailContentType = thumbnailType;
            photo.ThumbnailData = thumbnail;
        }, cancellationToken);
        return urls.Create(productId, version);
    }

    public Task DeleteAsync(int productId, CancellationToken cancellationToken) =>
        ChangeAsync(productId, hadPhoto => hadPhoto ? (L.Msg("Photo"), L.Msg("Removed")) : throw new NotFoundException("This product has no photo."), photo =>
        {
            photo.ContentType = null;
            photo.ImageData = null;
            photo.ThumbnailContentType = null;
            photo.ThumbnailData = null;
        }, cancellationToken);

    public async Task<ImageFile?> ReadAsync(int productId, int version, ProductImageSize size, CancellationToken cancellationToken)
    {
        var image = db.ProductImages.AsNoTracking().Where(i => i.ProductId == productId && i.Version == version && i.ImageData != null);
        return size == ProductImageSize.Thumb
            ? await image.Select(i => new ImageFile(i.ThumbnailData!, i.ThumbnailContentType!)).SingleOrDefaultAsync(cancellationToken)
            : await image.Select(i => new ImageFile(i.ImageData!, i.ContentType!)).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Changes the photo under a lock and records it; describe gives the history entry from whether there was a photo,
    /// or throws to refuse. Returns the new version.
    /// </summary>
    private async Task<int> ChangeAsync(int productId, Func<bool, (string? Before, string After)> describe, Action<ProductImage> change, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await ProductLabelAsync(productId, cancellationToken);

        // Locked (a missing row's key too), so two changes at once can't both become the same version.
        var photo = await db.ProductImages
            .FromSql($"SELECT * FROM ProductImages WITH (UPDLOCK, HOLDLOCK) WHERE ProductId = {productId}")
            .SingleOrDefaultAsync(cancellationToken);
        var (before, after) = describe(photo?.ImageData is not null);
        if (photo is null)
        {
            photo = new ProductImage { ProductId = productId };
            db.ProductImages.Add(photo);
        }

        change(photo);
        photo.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(PhotoChanged(productId, product, before, after), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return photo.Version;
    }

    private static string Check(string field, byte[] data, int maxBytes)
    {
        if (data.Length == 0)
        {
            throw new ValidationException(field, "Choose a picture.");
        }

        if (data.Length > maxBytes)
        {
            throw new ValidationException(field, string.Create(CultureInfo.InvariantCulture, $"The picture is too large; it can be at most {maxBytes / 1024} KB."));
        }

        return ImageFiles.Detect(data) ?? throw new ValidationException(field, "Choose a JPEG, PNG or WebP picture.");
    }

    private async Task<string> ProductLabelAsync(int productId, CancellationToken cancellationToken) =>
        await db.Products.Where(p => p.Id == productId).Select(p => p.Sku + " · " + p.Name).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException($"Product {productId} was not found.");

    /// <summary>A photo change, in the product's own history (the picture itself is never copied into the trail).</summary>
    private static AuditEvent PhotoChanged(int productId, string label, string? before, string after) => new(
        AuditAction.Updated, L.Msg("Product"), productId.ToString(CultureInfo.InvariantCulture), label, Changes: [new AuditChange(L.Msg("Photo"), before, after)]);
}
