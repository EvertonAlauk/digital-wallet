using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DigitalWallet.Bff.Identity;

namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>
/// Stands in for the identity provider. Records the calls and returns what each test configures. No network is used.
/// </summary>
public sealed class FakeIdentityService : IIdentityService
{
    private int _passwordSignInCalls;
    private int _refreshCalls;

    public FakeIdentityService()
    {
        Reset();
    }

    /// <summary>Answers password sign-ins. By default, any user gets tokens for a user with the matching name.</summary>
    public Func<string, string, IdentityResult> OnPasswordSignIn { get; set; } = null!;

    /// <summary>Answers refreshes. By default, the refresh rotates the tokens.</summary>
    public Func<string, IdentityResult> OnRefresh { get; set; } = null!;

    /// <summary>When set, refreshes wait for this task before answering. Used to make concurrent requests overlap.</summary>
    public Task? RefreshGate { get; set; }

    /// <summary>Completes when the first refresh starts.</summary>
    public TaskCompletionSource RefreshStarted { get; private set; } = NewCompletionSource();

    public ConcurrentQueue<string> SignOutRefreshTokens { get; } = new();

    public int PasswordSignInCalls => Volatile.Read(ref _passwordSignInCalls);

    public int RefreshCalls => Volatile.Read(ref _refreshCalls);

    /// <summary>Returns the fake to its defaults, so tests in the same class do not affect each other.</summary>
    public void Reset()
    {
        OnPasswordSignIn = (username, _) => Success(FakeTokens.Create(
            sub: $"{username}-id",
            username: username,
            roles: username == "admin" ? ["admin", "user"] : ["user"],
            expiresInSeconds: 300,
            refreshToken: $"refresh-{username}"));

        OnRefresh = _ => Success(FakeTokens.Create(
            sub: "alice-id",
            username: "alice",
            roles: ["user"],
            expiresInSeconds: 300,
            refreshToken: "refresh-rotated"));

        RefreshGate = null;
        RefreshStarted = NewCompletionSource();
        Interlocked.Exchange(ref _passwordSignInCalls, 0);
        Interlocked.Exchange(ref _refreshCalls, 0);
        SignOutRefreshTokens.Clear();
    }

    public Task<IdentityResult> PasswordSignInAsync(string username, string password, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _passwordSignInCalls);
        return Task.FromResult(OnPasswordSignIn(username, password));
    }

    public async Task<IdentityResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refreshCalls);
        RefreshStarted.TrySetResult();
        if (RefreshGate is { } gate)
        {
            await gate;
        }

        return OnRefresh(refreshToken);
    }

    public Task SignOutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        SignOutRefreshTokens.Enqueue(refreshToken);
        return Task.CompletedTask;
    }

    public static IdentityResult Success(IdentityTokens tokens) => new(IdentityOutcome.Success, tokens);

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Builds access tokens shaped like Keycloak's. The BFF reads their claims without checking the signature.</summary>
public static class FakeTokens
{
    public static IdentityTokens Create(
        string sub,
        string username,
        string[] roles,
        int expiresInSeconds,
        string refreshToken,
        string? name = null,
        string? email = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sub"] = sub,
            ["preferred_username"] = username,
            ["name"] = name,
            ["email"] = email,
            ["realm_access"] = new { roles },
        };

        var accessToken = $"{Encode(new { alg = "RS256", typ = "JWT" })}.{Encode(payload)}.signature-is-not-checked";
        return new IdentityTokens(accessToken, refreshToken, expiresInSeconds);
    }

    private static string Encode(object value) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
