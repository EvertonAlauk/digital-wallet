using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Bff.Auth;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.BffTests;

/// <summary>Kept in its own class: the rate limit is per client address, and the other classes share one address.</summary>
public class RateLimitTests(BffFactory factory) : IClassFixture<BffFactory>
{
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task Login_BeyondTheLimitPerClient_Returns429()
    {
        factory.Keycloak.Respond = _ => FakeKeycloak.Json(HttpStatusCode.BadRequest, FakeKeycloak.Error("invalid_grant"));

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < AuthRateLimits.PermitLimit + 1; attempt++)
        {
            statuses.Add((await _http.PostAsJsonAsync("/auth/login", new LoginRequest("alice", "wrong"))).StatusCode);
        }

        statuses.Take(AuthRateLimits.PermitLimit).Should().OnlyContain(status => status == HttpStatusCode.Unauthorized);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);
    }
}
