using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace DigitalWallet.Bff.Sessions;

/// <summary>The user as the frontend sees it. It is built from token claims and contains no token.</summary>
public sealed record SessionUser(string Sub, string Username, string? Name, string? Email, IReadOnlyList<string> Roles)
{
    /// <summary>
    /// Reads the claims of an access token that Keycloak issued over the back channel. The signature is not checked
    /// here: the API validates every token it receives. The BFF only needs the claims to describe the user.
    /// </summary>
    public static SessionUser? FromAccessToken(string accessToken)
    {
        var segments = accessToken.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(DecodeBase64Url(segments[1]));
            var root = document.RootElement;

            var sub = GetString(root, SessionClaims.Subject);
            var username = GetString(root, SessionClaims.Username);
            if (sub is null || username is null)
            {
                return null;
            }

            var roles = new List<string>();
            if (root.TryGetProperty("realm_access", out var realmAccess)
                && realmAccess.ValueKind == JsonValueKind.Object
                && realmAccess.TryGetProperty("roles", out var roleArray)
                && roleArray.ValueKind == JsonValueKind.Array)
            {
                roles.AddRange(roleArray.EnumerateArray()
                    .Where(role => role.ValueKind == JsonValueKind.String)
                    .Select(role => role.GetString()!));
            }

            return new SessionUser(sub, username, GetString(root, SessionClaims.Name), GetString(root, SessionClaims.Email), roles);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Reads the user back from the session principal. Returns null when there is no authenticated session.</summary>
    public static SessionUser? FromPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var sub = principal.FindFirst(SessionClaims.Subject)?.Value;
        var username = principal.FindFirst(SessionClaims.Username)?.Value;
        if (sub is null || username is null)
        {
            return null;
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
        return new SessionUser(
            sub,
            username,
            principal.FindFirst(SessionClaims.Name)?.Value,
            principal.FindFirst(SessionClaims.Email)?.Value,
            roles);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string DecodeBase64Url(string segment)
    {
        var base64 = segment.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
