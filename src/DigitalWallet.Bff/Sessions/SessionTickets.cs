using System.Globalization;
using System.Security.Claims;
using DigitalWallet.Bff.Auth;
using DigitalWallet.Bff.Identity;
using Microsoft.AspNetCore.Authentication;

namespace DigitalWallet.Bff.Sessions;

/// <summary>Builds the principal and the server-side tokens that the cookie handler stores in the session.</summary>
public static class SessionTickets
{
    public const string AccessToken = "access_token";

    public const string RefreshToken = "refresh_token";

    private const string ExpiresAt = "expires_at";

    public static ClaimsPrincipal CreatePrincipal(string sessionId, SessionUser user)
    {
        var identity = new ClaimsIdentity(SessionAuthentication.Scheme, SessionClaims.Username, ClaimTypes.Role);
        identity.AddClaim(new Claim(SessionClaims.SessionId, sessionId));
        identity.AddClaim(new Claim(SessionClaims.Subject, user.Sub));
        identity.AddClaim(new Claim(SessionClaims.Username, user.Username));
        if (user.Name is not null)
        {
            identity.AddClaim(new Claim(SessionClaims.Name, user.Name));
        }

        if (user.Email is not null)
        {
            identity.AddClaim(new Claim(SessionClaims.Email, user.Email));
        }

        foreach (var role in user.Roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    public static AuthenticationProperties CreateProperties(IdentityTokens tokens, DateTimeOffset now)
    {
        var properties = new AuthenticationProperties();
        UpdateTokens(properties, tokens, now);
        return properties;
    }

    /// <summary>Replaces the stored tokens, for example after a refresh. The refresh token changes on every refresh.</summary>
    public static void UpdateTokens(AuthenticationProperties properties, IdentityTokens tokens, DateTimeOffset now) =>
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = AccessToken, Value = tokens.AccessToken },
            new AuthenticationToken { Name = RefreshToken, Value = tokens.RefreshToken },
            new AuthenticationToken
            {
                Name = ExpiresAt,
                Value = now.AddSeconds(tokens.ExpiresInSeconds).ToString("O", CultureInfo.InvariantCulture),
            },
        ]);

    public static DateTimeOffset? AccessTokenExpiresAt(AuthenticationProperties properties) =>
        properties.GetTokenValue(ExpiresAt) is { } value
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
            ? expiresAt
            : null;
}
