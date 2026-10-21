namespace SalesApp.Core.Common;

/// <summary>
/// Recognises the picture formats browsers can show, from a file's first bytes rather than its name or the
/// browser's content type, which are both easy to fake.
/// </summary>
public static class ImageFiles
{
    /// <returns>The media type, or null for anything that isn't a JPEG, PNG or WebP picture.</returns>
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
