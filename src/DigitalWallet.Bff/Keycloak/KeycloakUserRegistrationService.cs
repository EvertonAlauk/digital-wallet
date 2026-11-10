using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalWallet.Bff.Common;
using DigitalWallet.Bff.Identity;
using Microsoft.Extensions.Options;

namespace DigitalWallet.Bff.Keycloak;

/// <summary>
/// Creates accounts through the Keycloak Admin REST API, using the registrar client's service account (client
/// credentials). The account is created enabled, with the "user" realm role. If the role cannot be given, the account
/// is removed again, so a half-created user never stays behind. Passwords are never logged.
/// </summary>
public sealed class KeycloakUserRegistrationService(HttpClient httpClient, IOptions<KeycloakOptions> options)
    : IUserRegistrationService
{
    private const string UserRole = "user";

    private readonly KeycloakOptions _options = options.Value;

    public async Task<RegistrationResult> RegisterAsync(NewUserAccount account, CancellationToken cancellationToken)
    {
        try
        {
            var token = await GetAdminTokenAsync(cancellationToken);
            if (token is null)
            {
                return new RegistrationResult(RegistrationOutcome.Misconfigured);
            }

            var created = await CreateUserAsync(account, token, cancellationToken);
            if (created.Outcome != RegistrationOutcome.Created || created.UserId is null)
            {
                return new RegistrationResult(created.Outcome);
            }

            if (!await GrantUserRoleAsync(created.UserId, token, cancellationToken))
            {
                await DeleteUserAsync(created.UserId, token, cancellationToken);
                return new RegistrationResult(RegistrationOutcome.Unavailable);
            }

            return new RegistrationResult(RegistrationOutcome.Created);
        }
        catch (HttpRequestException)
        {
            return new RegistrationResult(RegistrationOutcome.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's timeout surfaces as a cancellation: Keycloak did not answer in time.
            return new RegistrationResult(RegistrationOutcome.Unavailable);
        }
        catch (JsonException)
        {
            return new RegistrationResult(RegistrationOutcome.Unavailable);
        }
    }

    /// <summary>Client credentials grant. Returns null when the registrar credentials are refused.</summary>
    private async Task<string?> GetAdminTokenAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            _options.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.RegistrarClientId,
                ["client_secret"] = _options.RegistrarClientSecret,
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(cancellationToken);
        return payload?.AccessToken;
    }

    private async Task<CreateResult> CreateUserAsync(NewUserAccount account, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.AdminBaseUrl}/users")
        {
            Content = JsonContent.Create(new
            {
                username = account.Email,
                email = account.Email,
                firstName = account.FirstName,
                lastName = account.LastName,
                enabled = true,
                emailVerified = false,
                credentials = new[]
                {
                    new { type = "password", value = account.Password, temporary = false },
                },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        return response.StatusCode switch
        {
            HttpStatusCode.Created => new CreateResult(RegistrationOutcome.Created, UserIdFrom(response)),
            HttpStatusCode.Conflict => new CreateResult(RegistrationOutcome.AlreadyExists, null),
            HttpStatusCode.BadRequest => new CreateResult(RegistrationOutcome.Rejected, null),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new CreateResult(RegistrationOutcome.Misconfigured, null),
            _ => new CreateResult(RegistrationOutcome.Unavailable, null),
        };
    }

    private async Task<bool> GrantUserRoleAsync(string userId, string token, CancellationToken cancellationToken)
    {
        using var roleRequest = new HttpRequestMessage(HttpMethod.Get, $"{_options.AdminBaseUrl}/roles/{UserRole}");
        roleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var roleResponse = await httpClient.SendAsync(roleRequest, cancellationToken);
        if (!roleResponse.IsSuccessStatusCode)
        {
            return false;
        }

        var role = await roleResponse.Content.ReadFromJsonAsync<RolePayload>(cancellationToken);
        if (role?.Id is null || role.Name is null)
        {
            return false;
        }

        using var mapRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.AdminBaseUrl}/users/{userId}/role-mappings/realm")
        {
            Content = JsonContent.Create(new[] { new { id = role.Id, name = role.Name } }),
        };
        mapRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var mapResponse = await httpClient.SendAsync(mapRequest, cancellationToken);
        return mapResponse.IsSuccessStatusCode;
    }

    private async Task DeleteUserAsync(string userId, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_options.AdminBaseUrl}/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>Keycloak returns the new user's address in Location; its last segment is the id.</summary>
    private static string? UserIdFrom(HttpResponseMessage response) =>
        response.Headers.Location?.Segments.LastOrDefault()?.TrimEnd('/');

    private sealed record CreateResult(RegistrationOutcome Outcome, string? UserId);

    private sealed record TokenPayload([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record RolePayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);
}
