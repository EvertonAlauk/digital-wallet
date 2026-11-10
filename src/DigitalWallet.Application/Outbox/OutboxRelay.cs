using DigitalWallet.Application.Ports;

namespace DigitalWallet.Application.Outbox;

/// <summary>
/// Publishes pending outbox messages in order. Each message is marked published only after its publish succeeds.
/// If publishing fails, the exception propagates; the failed message and the rest of the batch stay pending.
/// </summary>
public sealed class OutboxRelay(IOutboxStore store, IMessagePublisher publisher, IClock clock)
{
    /// <summary>Publishes up to <paramref name="batchSize"/> pending messages. Returns how many were published.</summary>
    public async Task<int> PublishPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await store.GetPendingAsync(batchSize, cancellationToken);

        var published = 0;
        foreach (var message in pending)
        {
            // Publish, then mark. A crash between the two publishes the message again, which is why consumers are idempotent.
            await publisher.PublishAsync(message, cancellationToken);
            await store.MarkPublishedAsync(message.Id, clock.UtcNow, cancellationToken);
            published++;
        }

        return published;
    }
}
