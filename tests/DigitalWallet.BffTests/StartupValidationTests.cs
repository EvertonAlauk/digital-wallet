using DigitalWallet.Bff.Keycloak;
using DigitalWallet.Bff.Ui;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace DigitalWallet.BffTests;

public class StartupValidationTests
{
    [Fact]
    public void FrontendFolderThatDoesNotExist_FailsAtStartup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [StaticUi.ConfigurationKey] = "/no/such/folder" })
            .Build();

        var act = () => StaticUi.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Ui:StaticFilesPath*");
    }

    [Fact]
    public void NoFrontendFolderConfigured_PassesValidation()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => StaticUi.Validate(configuration);

        act.Should().NotThrow();
    }

    private static KeycloakOptions Valid() => new()
    {
        TokenEndpoint = "https://idp.example/token",
        LogoutEndpoint = "https://idp.example/logout",
        ClientId = "digital-wallet-bff",
        ClientSecret = "from-the-environment",
    };

    [Fact]
    public void ValidOptions_PassValidation()
    {
        var act = () => Valid().Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void MissingClientSecret_FailsAtStartup()
    {
        var options = Valid();
        options = new KeycloakOptions
        {
            TokenEndpoint = options.TokenEndpoint,
            LogoutEndpoint = options.LogoutEndpoint,
            ClientId = options.ClientId,
            ClientSecret = string.Empty,
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ClientSecret*");
    }

    [Fact]
    public void RelativeTokenEndpoint_FailsAtStartup()
    {
        var options = new KeycloakOptions
        {
            TokenEndpoint = "/token",
            LogoutEndpoint = "https://idp.example/logout",
            ClientId = "digital-wallet-bff",
            ClientSecret = "x",
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*TokenEndpoint*");
    }
}

public class HttpUrlTests
{
    [Theory]
    [InlineData("https://idp.example/token")]
    [InlineData("http://localhost:8081/realms/x")]
    public void AbsoluteHttpUrls_AreAccepted(string value) =>
        DigitalWallet.Bff.Common.HttpUrl.IsAbsoluteHttp(value).Should().BeTrue();

    [Theory]
    [InlineData("/token")]
    [InlineData("token")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://idp.example/token")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsRejected(string? value) =>
        DigitalWallet.Bff.Common.HttpUrl.IsAbsoluteHttp(value).Should().BeFalse();
}
