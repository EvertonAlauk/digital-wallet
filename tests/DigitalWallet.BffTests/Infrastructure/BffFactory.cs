using DigitalWallet.Bff.Keycloak;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>Runs the BFF with a fake Keycloak behind its token and logout endpoints.</summary>
public class BffFactory : WebApplicationFactory<Program>
{
    public const string TestClientSecret = "test-client-secret-value";

    public FakeKeycloak Keycloak { get; } = new();

    /// <summary>The ASP.NET Core environment the BFF runs in. Subclasses change it to test environment-specific behaviour.</summary>
    protected virtual string EnvironmentName => "Testing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("Keycloak:TokenEndpoint", "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/token");
        builder.UseSetting("Keycloak:LogoutEndpoint", "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/logout");
        builder.UseSetting("Keycloak:ClientId", "digital-wallet-bff");
        builder.UseSetting("Keycloak:ClientSecret", TestClientSecret);

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<KeycloakClient>().ConfigurePrimaryHttpMessageHandler(() => Keycloak);
        });
    }
}
