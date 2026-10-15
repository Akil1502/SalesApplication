using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SalesApp.Core.Entities;

namespace SalesApp.Api.Security;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user);
}

/// <summary>
/// Issues HS256 access tokens. Claim types are the short JWT names ("sub", "name") because inbound claim mapping is
/// switched off in Program.cs, so what we write is what User.FindFirst() sees. The token says who the user is, not
/// what they may do: their role and permissions are looked up on each request (PermissionClaimsTransformation), so
/// a change by an administrator applies at once rather than when the token expires.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(AppClaimTypes.UserId, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(AppClaimTypes.Name, user.FullName),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return (_handler.CreateToken(descriptor), expires);
    }
}

/// <summary>Claim type names used in our tokens.</summary>
public static class AppClaimTypes
{
    public const string Name = JwtRegisteredClaimNames.Name;
    public const string UserId = JwtRegisteredClaimNames.Sub;

    /// <summary>Added per request from the database, never written into the token.</summary>
    public const string Permission = "permission";

    /// <summary>Added per request from the database, never written into the token.</summary>
    public const string RoleName = "role_name";
}
