using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Bff.Auth;
using DigitalWallet.Bff.Keycloak;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.BffTests;

public class AuthEndpointsTests(BffFactory factory) : IClassFixture<BffFactory>
{
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTheTokens()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.OK, FakeKeycloak.Tokens("a-1", "r-1"));

        var response = await _http.PostAsJsonAsync("/auth/login", new LoginRequest("alice", "alice-password"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokens!.AccessToken.Should().Be("a-1");
        tokens.RefreshToken.Should().Be("r-1");
        tokens.ExpiresIn.Should().Be(300);
    }

    [Fact]
    public async Task Login_UsesThePasswordGrantWithTheClientCredentials()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.OK, FakeKeycloak.Tokens());

        await _http.PostAsJsonAsync("/auth/login", new LoginRequest("alice", "alice-password"));

        var sent = factory.Keycloak.Requests.Last();
        sent.Path.Should().EndWith("/protocol/openid-connect/token");
        sent.Form["grant_type"].Should().Be("password");
        sent.Form["username"].Should().Be("alice");
        sent.Form["password"].Should().Be("alice-password");
        sent.Form["client_id"].Should().Be("digital-wallet-bff");
        sent.Form["client_secret"].Should().Be(BffFactory.TestClientSecret);
    }

    [Fact]
    public async Task Login_NeverReturnsTheClientSecret()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.OK, FakeKeycloak.Tokens());

        var body = await (await _http.PostAsJsonAsync("/auth/login", new LoginRequest("alice", "x"))).Content.ReadAsStringAsync();

        body.Should().NotContain(BffFactory.TestClientSecret);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithAGenericMessage()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.BadRequest, FakeKeycloak.Error("invalid_grant"));

        var response = await _http.PostAsJsonAsync("/auth/login", new LoginRequest("alice", "wrong"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("Invalid username or password.");
        problem.Should().NotContain("invalid_grant", "Keycloak's error code is not exposed");
    }

    [Fact]
    public async Task Login_WhenTheBffSecretIsWrong_Returns500NotAuthenticationFailure()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.Unauthorized, FakeKeycloak.Error("invalid_client"));

        var response = await _http.PostAsJsonAsync("/auth/login", new LoginRequest("alice", "alice-password"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Login_WhenKeycloakIsDown_Returns503()
    {
        factory.Keycloak.Respond = _ => throw new HttpRequestException("connection refused");

        var response = await _http.PostAsJsonAsync("/auth/login", new LoginRequest("alice", "alice-password"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Login_WithMissingFields_Returns400WithoutCallingKeycloak()
    {
        var before = factory.Keycloak.Requests.Count;

        var response = await _http.PostAsJsonAsync("/auth/login", new LoginRequest("", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Keycloak.Requests.Count.Should().Be(before);
    }

    [Fact]
    public async Task Refresh_ReturnsNewTokensAndUsesTheRefreshGrant()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.OK, FakeKeycloak.Tokens("a-2", "r-2"));

        var response = await _http.PostAsJsonAsync("/auth/refresh", new RefreshRequest("r-1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TokenResponse>())!.RefreshToken.Should().Be("r-2");
        var sent = factory.Keycloak.Requests.Last();
        sent.Form["grant_type"].Should().Be("refresh_token");
        sent.Form["refresh_token"].Should().Be("r-1");
    }

    [Fact]
    public async Task Refresh_WithAnExpiredSession_Returns401()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.BadRequest, FakeKeycloak.Error("invalid_grant"));

        var response = await _http.PostAsJsonAsync("/auth/refresh", new RefreshRequest("reused"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("The session has expired. Sign in again.");
    }

    [Fact]
    public async Task Logout_SendsTheRefreshTokenToKeycloakAndReturns204()
    {
        var response = await _http.PostAsJsonAsync("/auth/logout", new LogoutRequest("r-9"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var sent = factory.Keycloak.Requests.Last();
        sent.Path.Should().EndWith("/protocol/openid-connect/logout");
        sent.Form["refresh_token"].Should().Be("r-9");
    }

    [Fact]
    public async Task Logout_SucceedsEvenWhenKeycloakIsDown()
    {
        factory.Keycloak.Respond = _ => throw new HttpRequestException("connection refused");

        var response = await _http.PostAsJsonAsync("/auth/logout", new LogoutRequest("r-9"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Health_IsAvailable()
    {
        var response = await _http.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
