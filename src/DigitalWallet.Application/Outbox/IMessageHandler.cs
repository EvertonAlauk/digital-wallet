namespace DigitalWallet.Application.Outbox;

/// <summary>A consumer for one message type. Must be idempotent: the same message can be delivered more than once.</summary>
public interface IMessageHandler
{
    string MessageType { get; }

    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}
