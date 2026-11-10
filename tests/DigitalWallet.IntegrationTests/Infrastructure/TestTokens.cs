using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace DigitalWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Creates access tokens locally, signed with a test key. No Keycloak is involved: the API validates these tokens with
/// the same rules it applies to Keycloak's tokens (issuer, audience, signature, lifetime).
/// </summary>
public static class TestTokens
{
    public const string Issuer = "https://issuer.test/realms/digital-wallet";

    public const string Audience = "digital-wallet-api";

    public static readonly RsaSecurityKey SigningKey = CreateKey();

    /// <summary>A key the API does not trust. Tokens signed with it must be refused.</summary>
    public static readonly RsaSecurityKey UntrustedKey = CreateKey();

    public static string Create(
        Guid subject,
        IEnumerable<string>? roles = null,
        string? issuer = null,
        string? audience = null,
        DateTimeOffset? expires = null,
        RsaSecurityKey? signingKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        var realmAccess = JsonSerializer.Serialize(new { roles = (roles ?? []).ToArray() });

        var claims = new List<Claim>
        {
            new("sub", subject.ToString()),
            new("realm_access", realmAccess, JsonClaimValueTypes.Json),
        };

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: (expires ?? now.AddMinutes(5)).AddMinutes(-10).UtcDateTime,
            expires: (expires ?? now.AddMinutes(5)).UtcDateTime,
            signingCredentials: new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static RsaSecurityKey CreateKey() => new(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString() };
}
