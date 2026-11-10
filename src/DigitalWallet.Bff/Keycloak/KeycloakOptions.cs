using DigitalWallet.Bff.Common;

namespace DigitalWallet.Bff.Keycloak;

/// <summary>
/// Keycloak connection settings. <see cref="ClientSecret"/> must come from the environment or user secrets,
/// never from the repository.
/// </summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string TokenEndpoint { get; init; } = string.Empty;

    public string LogoutEndpoint { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Client that creates user accounts through the Admin API. Its service account has manage-users.</summary>
    public string RegistrarClientId { get; init; } = string.Empty;

    public string RegistrarClientSecret { get; init; } = string.Empty;

    /// <summary>Base of the Admin REST API of the realm, for example http://localhost:8081/admin/realms/digital-wallet.</summary>
    public string AdminBaseUrl { get; init; } = string.Empty;

    /// <summary>Throws when a setting is missing, so the BFF does not start half-configured.</summary>
    public void Validate()
    {
        if (!HttpUrl.IsAbsoluteHttp(TokenEndpoint))
        {
            throw new InvalidOperationException("Keycloak:TokenEndpoint must be an absolute http or https URL.");
        }

        if (!HttpUrl.IsAbsoluteHttp(LogoutEndpoint))
        {
            throw new InvalidOperationException("Keycloak:LogoutEndpoint must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("Keycloak:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "Keycloak:ClientSecret is required. Set it through the environment or user secrets, not in appsettings.");
        }

        if (!HttpUrl.IsAbsoluteHttp(AdminBaseUrl))
        {
            throw new InvalidOperationException("Keycloak:AdminBaseUrl must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(RegistrarClientId))
        {
            throw new InvalidOperationException("Keycloak:RegistrarClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(RegistrarClientSecret))
        {
            throw new InvalidOperationException(
                "Keycloak:RegistrarClientSecret is required. Set it through the environment or user secrets, not in appsettings.");
        }
    }
}
