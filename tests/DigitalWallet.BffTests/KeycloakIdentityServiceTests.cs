using System.Net;
using DigitalWallet.Bff.Identity;
using DigitalWallet.Bff.Keycloak;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DigitalWallet.BffTests;

public class KeycloakIdentityServiceTests
{
    private readonly FakeKeycloak _keycloak = new();
    private readonly KeycloakIdentityService _service;

    public KeycloakIdentityServiceTests()
    {
        var options = Options.Create(new KeycloakOptions
        {
            TokenEndpoint = "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/token",
            LogoutEndpoint = "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/logout",
            ClientId = "digital-wallet-bff",
            ClientSecret = BffFactory.TestClientSecret,
        });
        _service = new KeycloakIdentityService(new HttpClient(_keycloak), options);
    }

    [Fact]
    public async Task PasswordSignIn_UsesThePasswordGrantWithTheClientCredentials()
    {
        await _service.PasswordSignInAsync("alice", "alice-password", CancellationToken.None);

        var sent = _keycloak.Requests.Single();
        sent.Path.Should().EndWith("/protocol/openid-connect/token");
        sent.Form["grant_type"].Should().Be("password");
        sent.Form["username"].Should().Be("alice");
        sent.Form["password"].Should().Be("alice-password");
        sent.Form["scope"].Should().Be("openid");
        sent.Form["client_id"].Should().Be("digital-wallet-bff");
        sent.Form["client_secret"].Should().Be(BffFactory.TestClientSecret);
    }

    [Fact]
    public async Task PasswordSignIn_WithValidCredentials_ReturnsTheTokens()
    {
        _keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.OK, FakeKeycloak.Tokens("a-1", "r-1"));

        var result = await _service.PasswordSignInAsync("alice", "alice-password", CancellationToken.None);

        result.Outcome.Should().Be(IdentityOutcome.Success);
        result.Tokens!.AccessToken.Should().Be("a-1");
        result.Tokens.RefreshToken.Should().Be("r-1");
        result.Tokens.ExpiresInSeconds.Should().Be(300);
    }

    [Fact]
    public async Task PasswordSignIn_WithAWrongPassword_IsRejected()
    {
        _keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.BadRequest, FakeKeycloak.Error("invalid_grant"));

        var result = await _service.PasswordSignInAsync("alice", "wrong", CancellationToken.None);

        result.Outcome.Should().Be(IdentityOutcome.Rejected);
        result.Tokens.Should().BeNull();
    }

    [Fact]
    public async Task PasswordSignIn_WhenTheClientSecretIsWrong_IsMisconfigured()
    {
        _keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.Unauthorized, FakeKeycloak.Error("invalid_client"));

        var result = await _service.PasswordSignInAsync("alice", "alice-password", CancellationToken.None);

        result.Outcome.Should().Be(IdentityOutcome.Misconfigured);
    }

    [Fact]
    public async Task PasswordSignIn_WhenKeycloakIsUnreachable_IsUnavailable()
    {
        _keycloak.Respond = _ => throw new HttpRequestException("connection refused");

        var result = await _service.PasswordSignInAsync("alice", "alice-password", CancellationToken.None);

        result.Outcome.Should().Be(IdentityOutcome.Unavailable);
    }

    [Fact]
    public async Task PasswordSignIn_WhenKeycloakDoesNotAnswerInTime_IsUnavailable()
    {
        // HttpClient reports its timeout as a cancellation, which is not the caller's cancellation.
        _keycloak.Respond = _ => throw new TaskCanceledException("timeout");

        var result = await _service.PasswordSignInAsync("alice", "alice-password", CancellationToken.None);

        result.Outcome.Should().Be(IdentityOutcome.Unavailable);
    }

    [Fact]
    public async Task Refresh_UsesTheRefreshGrant()
    {
        _keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.OK, FakeKeycloak.Tokens("a-2", "r-2"));

        var result = await _service.RefreshAsync("r-1", CancellationToken.None);

        result.Outcome.Should().Be(IdentityOutcome.Success);
        result.Tokens!.RefreshToken.Should().Be("r-2");
        var sent = _keycloak.Requests.Single();
        sent.Form["grant_type"].Should().Be("refresh_token");
        sent.Form["refresh_token"].Should().Be("r-1");
    }

    [Fact]
    public async Task Refresh_WithAReusedToken_IsRejected()
    {
        _keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.BadRequest, FakeKeycloak.Error("invalid_grant"));

        var result = await _service.RefreshAsync("reused", CancellationToken.None);

        result.Outcome.Should().Be(IdentityOutcome.Rejected);
    }

    [Fact]
    public async Task SignOut_SendsTheRefreshTokenToTheLogoutEndpoint()
    {
        await _service.SignOutAsync("r-9", CancellationToken.None);

        var sent = _keycloak.Requests.Single();
        sent.Path.Should().EndWith("/protocol/openid-connect/logout");
        sent.Form["refresh_token"].Should().Be("r-9");
    }

    [Fact]
    public async Task SignOut_SucceedsEvenWhenKeycloakIsDown()
    {
        _keycloak.Respond = _ => throw new HttpRequestException("connection refused");

        var act = () => _service.SignOutAsync("r-9", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TheResult_DoesNotPrintItsTokens()
    {
        _keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.OK, FakeKeycloak.Tokens("secret-access", "secret-refresh"));

        var result = await _service.PasswordSignInAsync("alice", "alice-password", CancellationToken.None);

        result.ToString().Should().NotContain("secret-access").And.NotContain("secret-refresh");
        result.Tokens!.ToString().Should().NotContain("secret-access").And.NotContain("secret-refresh");
    }
}
