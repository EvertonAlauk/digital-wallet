using DigitalWallet.Application.Ports;

namespace DigitalWallet.Application.Idempotency;

public enum IdempotencyDecisionKind
{
    /// <summary>The key is new (or expired). The caller must process the request and then complete or release the key.</summary>
    Acquired,

    /// <summary>The key was completed by an earlier request with the same payload. Replay its stored response.</summary>
    Replay,

    /// <summary>Another request with this key is still processing. It must not be processed again.</summary>
    InProgress,

    /// <summary>The key was used before with a different payload.</summary>
    PayloadMismatch,
}

public sealed record IdempotencyDecision(IdempotencyDecisionKind Kind, StoredResponse? Response = null);

/// <summary>
/// Decides what a request with an Idempotency-Key must do. Keys live for 24 hours.
/// Only one request can acquire a key: the insert is atomic, and everyone else reads the existing entry.
/// </summary>
public sealed class IdempotencyService(IIdempotencyRepository repository, IClock clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public const int MaxKeyLength = 255;

    public async Task<IdempotencyDecision> AcquireAsync(string key, string requestHash, CancellationToken cancellationToken)
    {
        // Two attempts: an expired entry is removed, then the key is inserted again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var now = clock.UtcNow;
            var entry = new IdempotencyEntry(key, requestHash, now, now + Retention, Response: null);

            if (await repository.TryInsertAsync(entry, cancellationToken))
            {
                return new IdempotencyDecision(IdempotencyDecisionKind.Acquired);
            }

            var existing = await repository.FindAsync(key, cancellationToken);
            if (existing is null)
            {
                // Deleted between the insert and the read (expired and removed by another request). Try again.
                continue;
            }

            if (existing.ExpiresAt <= now)
            {
                await repository.DeleteIfExpiredAsync(key, now, cancellationToken);
                continue;
            }

            if (existing.RequestHash != requestHash)
            {
                return new IdempotencyDecision(IdempotencyDecisionKind.PayloadMismatch);
            }

            return existing.Response is { } response
                ? new IdempotencyDecision(IdempotencyDecisionKind.Replay, response)
                : new IdempotencyDecision(IdempotencyDecisionKind.InProgress);
        }

        // Still contended after the retry: report it as in progress rather than loop.
        return new IdempotencyDecision(IdempotencyDecisionKind.InProgress);
    }

    public Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken) =>
        repository.CompleteAsync(key, response, cancellationToken);

    /// <summary>Removes a key whose request did not succeed, so that a retry is processed again.</summary>
    public Task ReleaseAsync(string key, CancellationToken cancellationToken) =>
        repository.DeleteAsync(key, cancellationToken);
}
