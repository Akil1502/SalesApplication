namespace SalesApp.AI.OrderEntry;

public enum OrderFileKind
{
    Pdf,
    Image,
}

public sealed record OrderFileType(OrderFileKind Kind, string MediaType);

/// <summary>
/// Identifies an uploaded file from its first bytes, not its name or the browser's content type, which are both
/// easy to fake. Only formats Claude can read are accepted.
/// </summary>
public static class OrderFile
{
    public const int MaxPdfBytes = 10 * 1024 * 1024;
    public const int MaxImageBytes = 5 * 1024 * 1024;

    public static OrderFileType? Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            return new(OrderFileKind.Pdf, "application/pdf");
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return new(OrderFileKind.Image, "image/jpeg");
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return new(OrderFileKind.Image, "image/png");
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return new(OrderFileKind.Image, "image/gif");
        }

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return new(OrderFileKind.Image, "image/webp");
        }

        return null;
    }
}
