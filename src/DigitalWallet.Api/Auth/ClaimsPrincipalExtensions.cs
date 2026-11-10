using System.Security.Claims;

namespace DigitalWallet.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity: the "sub" claim, which Keycloak sets to the user's id. Never taken from the request.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value;

        return Guid.TryParse(subject, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("The access token has no valid subject.");
    }
}
