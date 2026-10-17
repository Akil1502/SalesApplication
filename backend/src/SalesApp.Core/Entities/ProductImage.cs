namespace SalesApp.Core.Entities;

/// <summary>
/// A product's photo: one per product, in two sizes. The browser shrinks the picture before uploading it (a large
/// one for the product page, a small one for lists and pickers), so the server stores and serves bytes as they
/// are. It lives in its own table so reading products never drags the pictures along. Removing the photo keeps the
/// row (without a picture) so the version keeps counting up: a photo added later never reuses an address a browser
/// has cached.
/// </summary>
public sealed class ProductImage : IAuditable
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>image/jpeg, image/png or image/webp, read from the file's first bytes; null once removed.</summary>
    public string? ContentType { get; set; }

    public byte[]? ImageData { get; set; }

    public string? ThumbnailContentType { get; set; }

    public byte[]? ThumbnailData { get; set; }

    /// <summary>
    /// Goes up with every new photo. Picture addresses carry it, so browsers can keep a picture for a year and still
    /// fetch the new one the moment it changes.
    /// </summary>
    public int Version { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
