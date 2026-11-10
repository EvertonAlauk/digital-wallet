using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DigitalWallet.Bff.Sessions;

/// <summary>
/// Keeps the session tickets, tokens included, on the server. The cookie only carries a reference to the ticket.
/// Sessions live in this process: a restart ends them. To share sessions between instances, implement
/// <see cref="ITicketStore"/> with Redis and register it instead (see ADR 0010).
/// </summary>
public sealed class InMemoryTicketStore(TimeProvider? timeProvider = null) : ITicketStore
{
    private readonly ConcurrentDictionary<string, AuthenticationTicket> _tickets = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>The number of stored tickets, including ones that expired but were not read again yet.</summary>
    public int Count => _tickets.Count;

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = ticket.Principal.FindFirst(SessionClaims.SessionId)?.Value
            ?? throw new InvalidOperationException("The session ticket has no session identifier.");
        _tickets[key] = ticket;
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        _tickets[key] = ticket;
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!_tickets.TryGetValue(key, out var ticket))
        {
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        if (ticket.Properties.ExpiresUtc is { } expiresAt && expiresAt <= _time.GetUtcNow())
        {
            _tickets.TryRemove(key, out _);
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        return Task.FromResult<AuthenticationTicket?>(ticket);
    }

    public Task RemoveAsync(string key)
    {
        _tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
