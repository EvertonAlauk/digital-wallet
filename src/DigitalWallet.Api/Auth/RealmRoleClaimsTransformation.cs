using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace DigitalWallet.Api.Auth;

/// <summary>
/// Keycloak puts realm roles in one claim: <c>realm_access = {"roles": ["user", ...]}</c>. This copies them into
/// standard role claims, so policies can use <c>RequireRole</c>. Runs once per identity: a second run adds nothing.
/// </summary>
public sealed class RealmRoleClaimsTransformation : IClaimsTransformation
{
    public const string RealmAccessClaim = "realm_access";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identities.FirstOrDefault(identity => identity.IsAuthenticated);
        if (identity is null)
        {
            return Task.FromResult(principal);
        }

        var realmAccess = identity.FindFirst(RealmAccessClaim);
        if (realmAccess is null || identity.HasClaim(claim => claim.Type == ClaimTypes.Role))
        {
            return Task.FromResult(principal);
        }

        using var document = JsonDocument.Parse(realmAccess.Value);
        if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in roles.EnumerateArray())
            {
                var name = role.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, name));
                }
            }
        }

        return Task.FromResult(principal);
    }
}
