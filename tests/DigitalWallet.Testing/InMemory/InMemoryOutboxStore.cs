using DigitalWallet.Application.Outbox;

namespace DigitalWallet.Testing.InMemory;

/// <summary>Outbox read side over <see cref="InMemoryDatabase"/>.</summary>
public sealed class InMemoryOutboxStore(InMemoryDatabase database) : IOutboxStore
{
    public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.GetPendingOutbox(limit));
    }

    public Task MarkPublishedAsync(Guid messageId, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        database.MarkOutboxPublished(messageId, publishedAt);
        return Task.CompletedTask;
    }
}
