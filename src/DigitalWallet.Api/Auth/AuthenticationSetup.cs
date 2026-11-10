using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace DigitalWallet.Api.Auth;

/// <summary>
/// The API is a pure resource server: it validates the access token and nothing else. Tokens are issued by Keycloak
/// (see ADR 0009). The API keeps no users, sessions or cookies.
/// </summary>
public static class AuthenticationSetup
{
    public const string AdminPolicy = "Admin";

    /// <summary>The audience every access token must carry (set by the Keycloak audience mapper).</summary>
    public const string DefaultAudience = "digital-wallet-api";

    public static IServiceCollection AddDigitalWalletAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var authority = configuration["Auth:Authority"];
        var metadataAddress = configuration["Auth:MetadataAddress"];
        var audience = configuration["Auth:Audience"] ?? DefaultAudience;

        // Outside tests the authority is required: a server that accepts tokens from nowhere is not secure by default.
        if (string.IsNullOrWhiteSpace(authority) && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Auth:Authority is required. Point it to the Keycloak realm.");
        }

        var issuer = configuration["Auth:Issuer"] ?? authority ?? string.Empty;
        var requireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                }

                options.RequireHttpsMetadata = requireHttpsMetadata;

                // Keep claim names as the token has them ("sub", "realm_access"), not the legacy .NET mapping.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddTransient<IClaimsTransformation, RealmRoleClaimsTransformation>();

        services.AddAuthorizationBuilder()
            // Secure by default: an endpoint without an explicit policy still requires an authenticated user.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireRole("admin"));

        return services;
    }
}
