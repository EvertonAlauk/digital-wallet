namespace DigitalWallet.Application.Idempotency;

/// <summary>
/// Persistence of idempotency keys. Each operation is atomic on its own. The decision logic lives in
/// <see cref="IdempotencyService"/>, so every implementation behaves the same way.
/// </summary>
public interface IIdempotencyRepository
{
    /// <summary>Inserts the entry only when the key does not exist. Returns true when this call created it.</summary>
    Task<bool> TryInsertAsync(IdempotencyEntry entry, CancellationToken cancellationToken);

    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes the key only if it has expired at <paramref name="now"/>. Returns true when it was deleted.</summary>
    Task<bool> DeleteIfExpiredAsync(string key, DateTimeOffset now, CancellationToken cancellationToken);

    Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
