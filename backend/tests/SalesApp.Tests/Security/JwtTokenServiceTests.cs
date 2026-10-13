using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SalesApp.Api.Security;
using SalesApp.Core.Entities;

namespace SalesApp.Tests.Security;

public sealed class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "SalesApp",
        Audience = "SalesApp.Web",
        SigningKey = "unit-test-signing-key-that-is-long-enough-123",
        LifetimeMinutes = 480,
    };

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly AppUser User = new()
    {
        Id = Guid.Parse("5b8f6c2e-1111-4a8e-9a77-0c2b7c1d0001"),
        Email = "priya@example.in",
        UserName = "priya@example.in",
        FullName = "Priya Raman",
    };

    private static JwtTokenService CreateService() =>
        new(Microsoft.Extensions.Options.Options.Create(Options), new FakeTimeProvider(Now));

    [Fact]
    public void Token_expires_after_the_configured_lifetime()
    {
        var (_, expiresAtUtc) = CreateService().CreateToken(User, ["SalesRep"]);

        Assert.Equal(Now.UtcDateTime.AddMinutes(480), expiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, expiresAtUtc.Kind);
    }

    [Fact]
    public async Task Token_carries_identity_and_role_claims_and_validates_with_the_same_key()
    {
        var (token, _) = CreateService().CreateToken(User, ["SalesManager", "SalesRep"]);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.SigningKey)),
            // The fake clock is in the past relative to the machine clock; lifetime is covered above.
            ValidateLifetime = false,
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal(User.Id.ToString(), jwt.Subject);
        Assert.Equal("Priya Raman", jwt.GetClaim(AppClaimTypes.Name).Value);
        Assert.Equal(
            ["SalesManager", "SalesRep"],
            jwt.Claims.Where(c => c.Type == AppClaimTypes.Role).Select(c => c.Value).Order());
    }

    [Fact]
    public async Task Token_signed_with_a_different_key_is_rejected()
    {
        var (token, _) = CreateService().CreateToken(User, []);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-completely-different-signing-key-0000000")),
            ValidateLifetime = false,
        });

        Assert.False(result.IsValid);
    }
}
