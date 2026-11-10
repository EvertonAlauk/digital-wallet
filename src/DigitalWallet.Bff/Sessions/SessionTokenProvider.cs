using System.Collections.Concurrent;
using DigitalWallet.Bff.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DigitalWallet.Bff.Sessions;

/// <summary>
/// Gives the access token of a session, refreshing it first when it is about to expire. Refreshes are single-flight
/// per session: concurrent requests of the same session wait for one refresh and share its result. Keycloak rotates
/// the refresh token on each refresh, so a second refresh with the old token would end the session.
/// </summary>
public sealed class SessionTokenProvider(ITicketStore store, IServiceScopeFactory scopes, TimeProvider? timeProvider = null)
{
    /// <summary>An access token that expires within this margin is refreshed before it is used.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // One gate per session. Requests of the same session queue on it, so only one refresh runs at a time.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns a valid access token for the session, or null when the session does not exist or cannot be refreshed.
    /// A failed refresh ends the session.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(string sessionId, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var ticket = await store.RetrieveAsync(sessionId);
            if (ticket is null)
            {
                _gates.TryRemove(sessionId, out _);
                return null;
            }

            var properties = ticket.Properties;
            var accessToken = properties.GetTokenValue(SessionTickets.AccessToken);
            var expiresAt = SessionTickets.AccessTokenExpiresAt(properties);
            if (accessToken is not null && expiresAt is { } expiry && expiry - _time.GetUtcNow() > RefreshMargin)
            {
                return accessToken;
            }

            var refreshToken = properties.GetTokenValue(SessionTickets.RefreshToken);
            var result = refreshToken is null ? null : await RefreshAsync(refreshToken, cancellationToken);
            if (result?.Outcome != IdentityOutcome.Success || result.Tokens is not { } tokens)
            {
                // The refresh token was refused or the identity provider is down: the session cannot continue.
                await RemoveSessionAsync(sessionId);
                return null;
            }

            // The session may have been ended while the refresh was running. It must not come back.
            if (await store.RetrieveAsync(sessionId) is null)
            {
                return null;
            }

            SessionTickets.UpdateTokens(properties, tokens, _time.GetUtcNow());
            await store.RenewAsync(sessionId, ticket);
            return tokens.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Ends a session. Waits for a refresh that is already running, so the session cannot be revived by it.</summary>
    public async Task EndSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await RemoveSessionAsync(sessionId);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RemoveSessionAsync(string sessionId)
    {
        await store.RemoveAsync(sessionId);
        _gates.TryRemove(sessionId, out _);
    }

    // The identity service is resolved per call: it is a typed HttpClient, which must not be held for the lifetime of this singleton.
    private async Task<IdentityResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        return await identity.RefreshAsync(refreshToken, cancellationToken);
    }
}
