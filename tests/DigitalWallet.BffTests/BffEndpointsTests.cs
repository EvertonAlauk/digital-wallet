using System.Net;
using System.Net.Http.Json;
using System.Text;
using DigitalWallet.Bff.Identity;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.BffTests;

public class BffEndpointsTests : IClassFixture<BffFactory>
{
    private readonly BffFactory factory;

    // Cookies are handled by the tests, so each assertion sees exactly what the BFF sent.
    private readonly HttpClient _http;

    public BffEndpointsTests(BffFactory factory)
    {
        this.factory = factory;
        this.factory.Identity.Reset();
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTheUser()
    {
        var response = await LoginAsync("alice", "alice-password");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserEnvelope>();
        body!.User.Sub.Should().Be("alice-id");
        body.User.Username.Should().Be("alice");
        body.User.Roles.Should().Equal("user");
    }

    [Fact]
    public async Task Login_ReturnsNoToken_InTheBody()
    {
        var response = await LoginAsync("alice", "alice-password");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("access_token").And.NotContain("refresh_token");
        body.Should().NotContain("eyJ", "a JWT must never reach the browser");
        body.Should().NotContain("refresh-alice");
    }

    [Fact]
    public async Task Login_SetsTheSessionCookie_WithTheRequiredAttributes()
    {
        var response = await LoginAsync("alice", "alice-password");

        var setCookie = SessionCookie.SetCookieOf(response);
        setCookie.Should().NotBeNull();
        var attributes = setCookie!.ToLowerInvariant();
        attributes.Should().Contain("httponly");
        attributes.Should().Contain("secure");
        attributes.Should().Contain("samesite=strict");
        attributes.Should().Contain("path=/");
        attributes.Should().NotContain("domain=");
    }

    [Fact]
    public async Task Login_CookieCarriesNoToken()
    {
        var response = await LoginAsync("alice", "alice-password");

        var value = SessionCookie.ValueOf(SessionCookie.SetCookieOf(response)!);
        value.Should().NotContain("eyJ");
        value.Should().NotContain("refresh-alice");
    }

    [Fact]
    public async Task Login_WithWrongCredentials_Returns401WithAGenericProblem_AndNoCookie()
    {
        factory.Identity.OnPasswordSignIn = (_, _) => new IdentityResult(IdentityOutcome.Rejected);

        var response = await LoginAsync("alice", "wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Usuário ou senha inválidos");
        body.Should().NotContain("invalid_grant");
        SessionCookie.SetCookieOf(response).Should().BeNull();
    }

    [Fact]
    public async Task Login_WhenIdentityProviderRejectsTheClientSecret_Returns500()
    {
        factory.Identity.OnPasswordSignIn = (_, _) => new IdentityResult(IdentityOutcome.Misconfigured);

        var response = await LoginAsync("alice", "alice-password");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Login_WhenIdentityProviderIsDown_Returns503()
    {
        factory.Identity.OnPasswordSignIn = (_, _) => new IdentityResult(IdentityOutcome.Unavailable);

        var response = await LoginAsync("alice", "alice-password");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Login_WithMissingFields_Returns400_WithoutCallingTheIdentityProvider()
    {
        var response = await LoginAsync("", "");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Identity.PasswordSignInCalls.Should().Be(0);
    }

    [Fact]
    public async Task Login_WithoutTheCsrfHeader_Returns403_AndNeverReachesTheIdentityProvider()
    {
        var response = await SendLoginAsync("alice", "alice-password", csrf: false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Identity.PasswordSignInCalls.Should().Be(0);
    }

    [Fact]
    public async Task Login_WithANonJsonBody_Returns415()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = new StringContent("username=alice&password=x", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Add("X-CSRF", "1");

        var response = await _http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Login_EachLogin_StartsANewSession_AndEndsTheOldOne()
    {
        var first = SessionCookie.ValueOf(SessionCookie.SetCookieOf(await LoginAsync("alice", "alice-password"))!);

        var secondResponse = await SendLoginAsync("alice", "alice-password", cookie: first);
        var second = SessionCookie.ValueOf(SessionCookie.SetCookieOf(secondResponse)!);

        second.Should().NotBe(first, "a login must never reuse the session identifier (session fixation)");
        (await GetSessionAsync(first)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetSessionAsync(second)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Session_WithoutACookie_Returns401()
    {
        var response = await GetSessionAsync(cookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Session_WithAForgedCookie_Returns401()
    {
        var response = await GetSessionAsync(cookie: "forged-value-without-a-session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Session_WithAValidCookie_ReturnsTheUser()
    {
        var cookie = await CookieOfLoginAsync("admin", "admin-password");

        var response = await GetSessionAsync(cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserEnvelope>();
        body!.User.Username.Should().Be("admin");
        body.User.Roles.Should().Contain("admin");
    }

    [Fact]
    public async Task Logout_EndsTheSession_DeletesTheCookie_AndEndsTheKeycloakSession()
    {
        var cookie = await CookieOfLoginAsync("alice", "alice-password");

        var response = await SendLogoutAsync(cookie);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var deletion = SessionCookie.SetCookieOf(response);
        deletion.Should().NotBeNull();
        deletion.Should().Contain("expires=");
        (await GetSessionAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Identity.SignOutRefreshTokens.Should().Contain("refresh-alice");
    }

    [Fact]
    public async Task Logout_IsIdempotent()
    {
        var cookie = await CookieOfLoginAsync("alice", "alice-password");

        var first = await SendLogoutAsync(cookie);
        var second = await SendLogoutAsync(cookie);
        var withoutCookie = await SendLogoutAsync(cookie: null);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        withoutCookie.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_WithoutTheCsrfHeader_Returns403_AndTheSessionSurvives()
    {
        var cookie = await CookieOfLoginAsync("alice", "alice-password");

        var response = await SendLogoutAsync(cookie, csrf: false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetSessionAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_IsAvailableWithoutASession()
    {
        var response = await _http.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        await SendLoginAsync(username, password);

    private async Task<string> CookieOfLoginAsync(string username, string password)
    {
        var response = await LoginAsync(username, password);
        return SessionCookie.ValueOf(SessionCookie.SetCookieOf(response)!);
    }

    private async Task<HttpResponseMessage> SendLoginAsync(string username, string password, bool csrf = true, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { username, password }),
        };
        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        if (cookie is not null)
        {
            SessionCookie.Attach(request, cookie);
        }

        return await _http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetSessionAsync(string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/bff/session");
        if (cookie is not null)
        {
            SessionCookie.Attach(request, cookie);
        }

        return await _http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendLogoutAsync(string? cookie, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout")
        {
            Content = JsonContent.Create(new { }),
        };
        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        if (cookie is not null)
        {
            SessionCookie.Attach(request, cookie);
        }

        return await _http.SendAsync(request);
    }

    private sealed record UserEnvelope(UserBody User);

    private sealed record UserBody(string Sub, string Username, string? Name, string? Email, string[] Roles);
}
