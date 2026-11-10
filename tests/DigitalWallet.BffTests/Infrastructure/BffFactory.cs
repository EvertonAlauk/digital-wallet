using DigitalWallet.Bff.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Forwarder;

namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>Runs the BFF with a fake identity provider. Subclasses change the environment and the settings for their tests.</summary>
public class BffFactory : WebApplicationFactory<Program>
{
    public const string TestClientSecret = "test-client-secret-value";

    public FakeIdentityService Identity { get; } = new();

    public FakeUserRegistrationService Registration { get; } = new();

    public StubApi Api { get; } = new();

    protected virtual string EnvironmentName => "Testing";

    protected virtual string LoginPermitLimit => "100000";

    /// <summary>The frontend folder to serve. Null leaves the static frontend disabled, as in production by default.</summary>
    protected virtual string? UiStaticFilesPath => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("Keycloak:TokenEndpoint", "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/token");
        builder.UseSetting("Keycloak:LogoutEndpoint", "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/logout");
        builder.UseSetting("Keycloak:ClientId", "digital-wallet-bff");
        builder.UseSetting("Keycloak:ClientSecret", TestClientSecret);
        builder.UseSetting("Keycloak:RegistrarClientId", "digital-wallet-registrar");
        builder.UseSetting("Keycloak:RegistrarClientSecret", "test-registrar-secret");
        builder.UseSetting("Keycloak:AdminBaseUrl", "http://keycloak.test/admin/realms/digital-wallet");
        builder.UseSetting("Api:BaseUrl", "http://api.test");
        builder.UseSetting("RateLimit:GeneralPermitLimit", "100000");
        builder.UseSetting("RateLimit:LoginPermitLimit", LoginPermitLimit);

        if (UiStaticFilesPath is { } path)
        {
            builder.UseSetting("Ui:StaticFilesPath", path);
        }

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IIdentityService>(Identity));
            services.Replace(ServiceDescriptor.Singleton<IUserRegistrationService>(Registration));
            services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(new StubApiForwarderFactory(Api)));
        });
    }
}
