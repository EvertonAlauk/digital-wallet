using DigitalWallet.Bff.Auth;
using DigitalWallet.Bff.Identity;
using DigitalWallet.Bff.Sessions;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalWallet.BffTests;

/// <summary>Tests the refresh rules directly: no HTTP, the identity provider is the fake.</summary>
public class SessionTokenProviderTests
{
    private readonly InMemoryTicketStore _store = new();
    private readonly FakeIdentityService _identity = new();
    private readonly SessionTokenProvider _provider;

    public SessionTokenProviderTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdentityService>(_identity);
        _provider = new SessionTokenProvider(_store, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task AccessTokenThatIsStillValid_IsReturned_WithoutARefresh()
    {
        var sessionId = await StartSessionAsync(lifetime: TimeSpan.FromMinutes(5));

        var token = await _provider.GetAccessTokenAsync(sessionId, CancellationToken.None);

        token.Should().Be("access-old");
        _identity.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task AccessTokenExpiringWithinOneMinute_IsRefreshed_AndTheRotatedRefreshTokenIsStored()
    {
        var sessionId = await StartSessionAsync(lifetime: TimeSpan.FromSeconds(30));
        _identity.OnRefresh = _ => FakeIdentityService.Success(FakeTokens.Create("alice-id", "alice", ["user"], 300, "refresh-new"));

        var token = await _provider.GetAccessTokenAsync(sessionId, CancellationToken.None);

        token.Should().NotBe("access-old");
        _identity.RefreshCalls.Should().Be(1);
        var ticket = await _store.RetrieveAsync(sessionId);
        ticket!.Properties.GetTokenValue(SessionTickets.RefreshToken).Should().Be("refresh-new", "Keycloak rotates the refresh token");
    }

    [Fact]
    public async Task RefreshRefusedByTheIdentityProvider_EndsTheSession_AndReturnsNull()
    {
        var sessionId = await StartSessionAsync(lifetime: TimeSpan.FromSeconds(30));
        _identity.OnRefresh = _ => new IdentityResult(IdentityOutcome.Rejected);

        var token = await _provider.GetAccessTokenAsync(sessionId, CancellationToken.None);

        token.Should().BeNull();
        (await _store.RetrieveAsync(sessionId)).Should().BeNull();
    }

    [Fact]
    public async Task RefreshWhileTheIdentityProviderIsDown_EndsTheSession()
    {
        var sessionId = await StartSessionAsync(lifetime: TimeSpan.FromSeconds(30));
        _identity.OnRefresh = _ => new IdentityResult(IdentityOutcome.Unavailable);

        var token = await _provider.GetAccessTokenAsync(sessionId, CancellationToken.None);

        token.Should().BeNull();
        (await _store.RetrieveAsync(sessionId)).Should().BeNull();
    }

    [Fact]
    public async Task UnknownSession_ReturnsNull_WithoutCallingTheIdentityProvider()
    {
        var token = await _provider.GetAccessTokenAsync("no-such-session", CancellationToken.None);

        token.Should().BeNull();
        _identity.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentRequestsOfOneSession_TriggerASingleRefresh()
    {
        var sessionId = await StartSessionAsync(lifetime: TimeSpan.FromSeconds(30));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _identity.RefreshGate = gate.Task;
        _identity.OnRefresh = _ => FakeIdentityService.Success(new IdentityTokens("access-new", "refresh-new", 300));

        var requests = Enumerable.Range(0, 20)
            .Select(_ => _provider.GetAccessTokenAsync(sessionId, CancellationToken.None))
            .ToList();

        // The first refresh is now waiting on the gate, so the other 19 requests are queued behind it.
        await _identity.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult();
        var tokens = await Task.WhenAll(requests);

        _identity.RefreshCalls.Should().Be(1, "a second refresh with the old refresh token would end the session");
        tokens.Should().OnlyContain(token => token == "access-new");
    }

    [Fact]
    public async Task EndingASession_WaitsForARunningRefresh_AndTheSessionDoesNotComeBack()
    {
        var sessionId = await StartSessionAsync(lifetime: TimeSpan.FromSeconds(30));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _identity.RefreshGate = gate.Task;
        _identity.OnRefresh = _ => FakeIdentityService.Success(FakeTokens.Create("alice-id", "alice", ["user"], 300, "refresh-new"));

        var refresh = _provider.GetAccessTokenAsync(sessionId, CancellationToken.None);
        await _identity.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var ending = _provider.EndSessionAsync(sessionId);
        gate.SetResult();
        await Task.WhenAll(refresh, ending);

        (await _store.RetrieveAsync(sessionId)).Should().BeNull("the logout that ran during the refresh must win");
    }

    private async Task<string> StartSessionAsync(TimeSpan lifetime)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var user = new SessionUser("alice-id", "alice", null, null, ["user"]);
        var now = DateTimeOffset.UtcNow;
        var properties = SessionTickets.CreateProperties(
            new IdentityTokens("access-old", "refresh-old", (int)lifetime.TotalSeconds),
            now);
        properties.ExpiresUtc = now.AddHours(1);

        await _store.StoreAsync(new AuthenticationTicket(
            SessionTickets.CreatePrincipal(sessionId, user),
            properties,
            SessionAuthentication.Scheme));
        return sessionId;
    }
}
