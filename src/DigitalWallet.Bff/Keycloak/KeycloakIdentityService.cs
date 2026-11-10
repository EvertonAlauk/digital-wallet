using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalWallet.Bff.Identity;
using Microsoft.Extensions.Options;

namespace DigitalWallet.Bff.Keycloak;

/// <summary>
/// Calls Keycloak with the confidential client credentials (Direct Access Grants and the refresh_token grant).
/// Errors become outcomes, so Keycloak's error details never reach the HTTP layer or the logs.
/// The HttpClient timeout is set where the client is registered (Program.cs).
/// </summary>
public sealed class KeycloakIdentityService(HttpClient httpClient, IOptions<KeycloakOptions> options) : IIdentityService
{
    private readonly KeycloakOptions _options = options.Value;

    public Task<IdentityResult> PasswordSignInAsync(string username, string password, CancellationToken cancellationToken) =>
        PostTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid",
            },
            cancellationToken);

    public Task<IdentityResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            cancellationToken);

    public async Task SignOutAsync(string refreshToken, CancellationToken cancellationToken)
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
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request timed out. Same as above.
        }
    }

    private async Task<IdentityResult> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                _options.TokenEndpoint,
                new FormUrlEncodedContent(WithClientCredentials(form)),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(cancellationToken);
                if (payload?.AccessToken is null || payload.RefreshToken is null)
                {
                    return new IdentityResult(IdentityOutcome.Unavailable);
                }

                return new IdentityResult(
                    IdentityOutcome.Success,
                    new IdentityTokens(payload.AccessToken, payload.RefreshToken, payload.ExpiresIn));
            }

            // invalid_client means the BFF's own secret is wrong. Anything else is a refusal of the user's request.
            var error = await ReadErrorCodeAsync(response, cancellationToken);
            return error == "invalid_client"
                ? new IdentityResult(IdentityOutcome.Misconfigured)
                : new IdentityResult(IdentityOutcome.Rejected);
        }
        catch (HttpRequestException)
        {
            return new IdentityResult(IdentityOutcome.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's timeout surfaces as a cancellation: the identity provider did not answer in time.
            return new IdentityResult(IdentityOutcome.Unavailable);
        }
        catch (JsonException)
        {
            return new IdentityResult(IdentityOutcome.Unavailable);
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
            var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>(cancellationToken);
            return payload?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Keycloak's token endpoint response, read with the snake_case names it uses.</summary>
    private sealed record TokenPayload(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    /// <summary>Keycloak's error response. Only the error code is used.</summary>
    private sealed record ErrorPayload([property: JsonPropertyName("error")] string? Error);
}
