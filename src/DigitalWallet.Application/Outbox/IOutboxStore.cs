namespace DigitalWallet.Application.Outbox;

/// <summary>Read side of the outbox, used by the relay that publishes pending events.</summary>
public interface IOutboxStore
{
    /// <summary>Unpublished messages, oldest first.</summary>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int limit, CancellationToken cancellationToken);

    Task MarkPublishedAsync(Guid messageId, DateTimeOffset publishedAt, CancellationToken cancellationToken);
}
