using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Bff.Identity;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.BffTests;

/// <summary>A factory with a login limit of 3 per minute, so the test can reach it quickly.</summary>
public sealed class LoginLimitFactory : BffFactory
{
    public const int Limit = 3;

    protected override string LoginPermitLimit => Limit.ToString();
}

/// <summary>Kept in its own class: the rate limit is per client address, and the other classes share one address.</summary>
public class RateLimitTests(LoginLimitFactory factory) : IClassFixture<LoginLimitFactory>
{
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task Login_BeyondTheLimitPerClient_Returns429WithProblemDetails()
    {
        factory.Identity.OnPasswordSignIn = (_, _) => new IdentityResult(IdentityOutcome.Rejected);

        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < LoginLimitFactory.Limit + 1; attempt++)
        {
            responses.Add(await Login());
        }

        responses.Take(LoginLimitFactory.Limit).Select(r => r.StatusCode)
            .Should().OnlyContain(status => status == HttpStatusCode.Unauthorized);

        var last = responses.Last();
        last.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        last.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<HttpResponseMessage> Login()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { username = "alice", password = "wrong" }),
        };
        request.Headers.Add("X-CSRF", "1");
        return await _http.SendAsync(request);
    }
}
