using System.Text;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using RabbitMQ.Client;

namespace DigitalWallet.Infrastructure.Messaging.RabbitMq;

/// <summary>Publishes outbox messages to RabbitMQ. Returns after the broker confirms the message.</summary>
public sealed class RabbitMqPublisher(RabbitMqPublishChannel channel) : IMessagePublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = message.Id.ToString(),
            Type = message.Type,
            Headers = new Dictionary<string, object?> { [RetryPolicy.AttemptHeader] = 0 },
        };

        return channel.PublishAsync(
            QueueNames.For(message.Type),
            properties,
            Encoding.UTF8.GetBytes(message.Payload),
            cancellationToken);
    }
}
