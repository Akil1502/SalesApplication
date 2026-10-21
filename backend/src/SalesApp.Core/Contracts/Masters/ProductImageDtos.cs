namespace SalesApp.Core.Contracts.Masters;

/// <summary>
/// Where to load a product's photo from. The addresses are signed, so a plain &lt;img&gt; tag can load them without
/// the sign-in token, and they change with every new photo (Version), so browsers cache each one for good.
/// </summary>
public sealed record ProductImageDto(int ProductId, int Version, string ImageUrl, string ThumbnailUrl);

public enum ProductImageSize
{
    Full,
    Thumb,
}
