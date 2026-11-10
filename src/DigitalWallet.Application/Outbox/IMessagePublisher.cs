namespace DigitalWallet.Application.Outbox;

/// <summary>
/// Delivers one message to the messaging infrastructure. Must throw when delivery fails, so the relay keeps the
/// message pending and tries again. Delivery is at least once, so consumers must be idempotent.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
