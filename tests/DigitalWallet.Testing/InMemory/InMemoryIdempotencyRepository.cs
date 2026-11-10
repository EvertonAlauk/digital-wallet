using DigitalWallet.Application.Idempotency;

namespace DigitalWallet.Testing.InMemory;

/// <summary>Idempotency store over <see cref="InMemoryDatabase"/>. Each operation is atomic under the database lock.</summary>
public sealed class InMemoryIdempotencyRepository(InMemoryDatabase database) : IIdempotencyRepository
{
    public Task<bool> TryInsertAsync(IdempotencyEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.TryInsertIdempotencyEntry(entry));
    }

    public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.FindIdempotencyEntry(key));
    }

    public Task<bool> DeleteIfExpiredAsync(string key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.DeleteExpiredIdempotencyEntry(key, now));
    }

    public Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        database.CompleteIdempotencyEntry(key, response);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        database.DeleteIdempotencyEntry(key);
        return Task.CompletedTask;
    }
}
