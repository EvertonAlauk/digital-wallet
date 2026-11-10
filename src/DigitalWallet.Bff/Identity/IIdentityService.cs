namespace DigitalWallet.Bff.Identity;

/// <summary>
/// The identity provider as the BFF uses it. Keycloak is the only implementation; tests use a fake. The tokens
/// returned here stay on the server, inside the session.
/// </summary>
public interface IIdentityService
{
    /// <summary>Exchanges the user's credentials for tokens (Direct Access Grants).</summary>
    Task<IdentityResult> PasswordSignInAsync(string username, string password, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for new tokens. Keycloak rotates the refresh token on every call.</summary>
    Task<IdentityResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Ends the identity provider's session of a refresh token. Best effort.</summary>
    Task SignOutAsync(string refreshToken, CancellationToken cancellationToken);
}

public enum IdentityOutcome
{
    Success,

    /// <summary>The credentials or the refresh token were refused. The reason is never shown to the client.</summary>
    Rejected,

    /// <summary>The BFF's own client credentials are wrong. This is a server-side configuration error.</summary>
    Misconfigured,

    /// <summary>The identity provider could not be reached or did not answer in time.</summary>
    Unavailable,
}

public sealed record IdentityResult(IdentityOutcome Outcome, IdentityTokens? Tokens = null);

/// <summary>Tokens issued by the identity provider. They are never serialized to a response or written to a log.</summary>
public sealed record IdentityTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds)
{
    /// <summary>Hides the token values, so a logged record cannot leak them.</summary>
    public override string ToString() => $"IdentityTokens(expires in {ExpiresInSeconds}s)";
}
