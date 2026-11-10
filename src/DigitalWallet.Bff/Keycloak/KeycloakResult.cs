using System.Text.Json.Serialization;

namespace DigitalWallet.Bff.Keycloak;

public enum KeycloakOutcome
{
    /// <summary>Keycloak issued tokens.</summary>
    Success,

    /// <summary>Keycloak refused the credentials or the refresh token. The reason is not exposed to the client.</summary>
    Rejected,

    /// <summary>The BFF's own client credentials are wrong. This is a server-side configuration error.</summary>
    Misconfigured,

    /// <summary>Keycloak could not be reached.</summary>
    Unavailable,
}

public sealed record KeycloakResult(KeycloakOutcome Outcome, TokenResponse? Tokens = null);

/// <summary>The tokens returned to the frontend. The frontend sends the access token to the API as a Bearer token.</summary>
public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType);

/// <summary>Keycloak's token endpoint response, read with the snake_case names it uses.</summary>
internal sealed record KeycloakTokenPayload(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string TokenType);

/// <summary>Keycloak's error response. Only the error code is used.</summary>
internal sealed record KeycloakErrorPayload(
    [property: JsonPropertyName("error")] string? Error);
