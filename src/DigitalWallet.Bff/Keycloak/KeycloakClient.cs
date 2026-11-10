using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace DigitalWallet.Bff.Keycloak;

/// <summary>
/// The only component that calls Keycloak. Uses the Direct Access Grants (password and refresh_token grants)
/// with the confidential client credentials. Errors are mapped to outcomes, so the HTTP layer never sees
/// Keycloak's error details.
/// </summary>
public sealed class KeycloakClient(HttpClient httpClient, IOptions<KeycloakOptions> options)
{
    private readonly KeycloakOptions _options = options.Value;

    public Task<KeycloakResult> PasswordGrantAsync(string username, string password, CancellationToken cancellationToken) =>
        PostTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid",
            },
            cancellationToken);

    public Task<KeycloakResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            cancellationToken);

    /// <summary>Ends the Keycloak session of the refresh token. Best effort: the result is not reported.</summary>
    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                _options.LogoutEndpoint,
                new FormUrlEncodedContent(WithClientCredentials(new Dictionary<string, string>
                {
                    ["refresh_token"] = refreshToken,
                })),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Keycloak is unreachable. The refresh token expires on its own, so the logout still succeeds for the client.
        }
    }

    private async Task<KeycloakResult> PostTokenRequestAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                _options.TokenEndpoint,
                new FormUrlEncodedContent(WithClientCredentials(form)),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<KeycloakTokenPayload>(cancellationToken);
                if (payload is null)
                {
                    return new KeycloakResult(KeycloakOutcome.Unavailable);
                }

                return new KeycloakResult(
                    KeycloakOutcome.Success,
                    new TokenResponse(payload.AccessToken, payload.RefreshToken, payload.ExpiresIn, payload.TokenType));
            }

            // invalid_client means the BFF's own secret is wrong. Anything else is a refusal of the user's request.
            var error = await ReadErrorCodeAsync(response, cancellationToken);
            return error == "invalid_client"
                ? new KeycloakResult(KeycloakOutcome.Misconfigured)
                : new KeycloakResult(KeycloakOutcome.Rejected);
        }
        catch (HttpRequestException)
        {
            return new KeycloakResult(KeycloakOutcome.Unavailable);
        }
    }

    private Dictionary<string, string> WithClientCredentials(Dictionary<string, string> form)
    {
        form["client_id"] = _options.ClientId;
        form["client_secret"] = _options.ClientSecret;
        return form;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<KeycloakErrorPayload>(cancellationToken);
            return payload?.Error;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
