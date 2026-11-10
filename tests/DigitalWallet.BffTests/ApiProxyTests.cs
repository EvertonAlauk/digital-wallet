using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DigitalWallet.Bff.Identity;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.BffTests;

public class ApiProxyTests : IClassFixture<BffFactory>
{
    private readonly BffFactory factory;
    private readonly HttpClient _http;
    private string _accessToken = string.Empty;

    public ApiProxyTests(BffFactory factory)
    {
        this.factory = factory;
        this.factory.Identity.Reset();
        this.factory.Api.Reset();
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
    }

    [Fact]
    public async Task ApiRequest_IsForwardedWithTheSessionsBearerToken_AndWithoutTheCookie()
    {
        var cookie = await StartSessionAsync(expiresIn: 300);

        var response = await CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sent = factory.Api.Requests.Single();
        sent.PathAndQuery.Should().Be("/accounts/123", "the /api prefix is removed for the API");
        sent.Authorization.Should().Be($"Bearer {_accessToken}");
        sent.HasCookie.Should().BeFalse("the session cookie must not reach the API");
    }

    [Fact]
    public async Task AdminRoute_KeepsItsApiPrefix()
    {
        var cookie = await StartSessionAsync(expiresIn: 300);

        await CallApiAsync(HttpMethod.Get, "/api/admin/accounts/123", cookie);

        factory.Api.Requests.Single().PathAndQuery.Should().Be("/api/admin/accounts/123");
    }

    [Fact]
    public async Task QueryString_IsForwarded()
    {
        var cookie = await StartSessionAsync(expiresIn: 300);

        await CallApiAsync(HttpMethod.Get, "/api/accounts?page=2", cookie);

        factory.Api.Requests.Single().PathAndQuery.Should().Be("/accounts?page=2");
    }

    [Fact]
    public async Task AuthorizationHeaderSentByTheBrowser_IsReplaced()
    {
        var cookie = await StartSessionAsync(expiresIn: 300);

        await CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie, authorization: "Bearer forged-by-the-browser");

        factory.Api.Requests.Single().Authorization.Should().Be($"Bearer {_accessToken}");
    }

    [Fact]
    public async Task PostBody_IsForwarded()
    {
        var cookie = await StartSessionAsync(expiresIn: 300);

        var response = await CallApiAsync(HttpMethod.Post, "/api/accounts", cookie, content: JsonContent.Create(new { }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Api.Requests.Single().Method.Should().Be("POST");
    }

    [Fact]
    public async Task WithoutASession_Returns401_AndNothingIsForwarded()
    {
        var response = await CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        factory.Api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WithAForgedCookie_Returns401_AndNothingIsForwarded()
    {
        var response = await CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie: "forged");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutTheCsrfHeader_Returns403_AndNothingIsForwarded()
    {
        var cookie = await StartSessionAsync(expiresIn: 300);

        var response = await CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie, csrf: false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TokenNearExpiry_IsRefreshedBeforeTheRequestIsForwarded()
    {
        var cookie = await StartSessionAsync(expiresIn: 30);
        var renewed = FakeTokens.Create("alice-id", "alice", ["user"], 300, "refresh-2").AccessToken;
        factory.Identity.OnRefresh = _ => new IdentityResult(IdentityOutcome.Success, FakeTokens.Create("alice-id", "alice", ["user"], 300, "refresh-2"));

        var response = await CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Identity.RefreshCalls.Should().Be(1);
        factory.Api.Requests.Single().Authorization.Should().Be($"Bearer {renewed}");
    }

    [Fact]
    public async Task RefreshRefused_Returns401_EndsTheSession_AndDeletesTheCookie()
    {
        var cookie = await StartSessionAsync(expiresIn: 30);
        factory.Identity.OnRefresh = _ => new IdentityResult(IdentityOutcome.Rejected);

        var response = await CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionCookie.SetCookieOf(response).Should().NotBeNull("the browser must drop the dead cookie");
        factory.Api.Requests.Should().BeEmpty();
        (await GetSessionAsync(cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConcurrentApiRequests_OfOneSessionNearExpiry_TriggerASingleRefresh()
    {
        var cookie = await StartSessionAsync(expiresIn: 30);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Identity.RefreshGate = gate.Task;
        factory.Identity.OnRefresh = _ => new IdentityResult(IdentityOutcome.Success, FakeTokens.Create("alice-id", "alice", ["user"], 300, "refresh-2"));

        var calls = Enumerable.Range(0, 10)
            .Select(_ => CallApiAsync(HttpMethod.Get, "/api/accounts/123", cookie))
            .ToList();

        await factory.Identity.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult();
        var responses = await Task.WhenAll(calls);

        factory.Identity.RefreshCalls.Should().Be(1, "a second refresh would be refused and end the session");
        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
    }

    /// <summary>Logs in through the BFF with tokens that expire in <paramref name="expiresIn"/> seconds, and returns the cookie.</summary>
    private async Task<string> StartSessionAsync(int expiresIn)
    {
        var tokens = FakeTokens.Create("alice-id", "alice", ["user"], expiresIn, "refresh-1");
        _accessToken = tokens.AccessToken;
        factory.Identity.OnPasswordSignIn = (_, _) => new IdentityResult(IdentityOutcome.Success, tokens);

        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { username = "alice", password = "alice-password" }),
        };
        request.Headers.Add("X-CSRF", "1");
        var response = await _http.SendAsync(request);

        return SessionCookie.ValueOf(SessionCookie.SetCookieOf(response)!);
    }

    private async Task<HttpResponseMessage> CallApiAsync(
        HttpMethod method,
        string path,
        string? cookie,
        bool csrf = true,
        string? authorization = null,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        if (cookie is not null)
        {
            SessionCookie.Attach(request, cookie);
        }

        if (authorization is not null)
        {
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        }

        return await _http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetSessionAsync(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/bff/session");
        SessionCookie.Attach(request, cookie);
        return await _http.SendAsync(request);
    }
}
