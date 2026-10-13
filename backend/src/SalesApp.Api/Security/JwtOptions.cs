using System.ComponentModel.DataAnnotations;

namespace SalesApp.Api.Security;

/// <summary>
/// JWT settings bound from the "Jwt" configuration section. The signing key is a secret:
/// it comes from dotnet user-secrets in development and environment variables in production,
/// never from a committed appsettings file.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "SalesApp";

    [Required]
    public string Audience { get; set; } = "SalesApp.Web";

    /// <summary>HMAC-SHA256 key; at least 32 characters (256 bits).</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(5, 24 * 60)]
    public int LifetimeMinutes { get; set; } = 8 * 60;
}
