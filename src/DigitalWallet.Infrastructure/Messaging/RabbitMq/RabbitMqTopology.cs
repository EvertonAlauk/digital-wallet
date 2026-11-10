using DigitalWallet.Application.Messaging;
using RabbitMQ.Client;

namespace DigitalWallet.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Declares the exchange and, for every message type, three queues:
/// <list type="bullet">
/// <item><c>{name}</c>: the main queue, consumed by the handler.</item>
/// <item><c>{name}-dlq</c>: the retry queue. Messages wait <see cref="RetryDelayMilliseconds"/> here, then return to the main queue.</item>
/// <item><c>{name}-dlt</c>: the dead-letter topic. Messages that failed every retry stay here for manual replay.</item>
/// </list>
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "wallet";

    public const int RetryDelayMilliseconds = 5000;

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            Exchange,
            ExchangeType.Direct,
            durable: true,
            cancellationToken: cancellationToken);

        foreach (var messageType in MessageTypes.All)
        {
            var queue = QueueNames.For(messageType);
            var retry = QueueNames.RetryFor(queue);
            var deadLetter = QueueNames.DeadLetterFor(queue);

            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(queue, Exchange, queue, cancellationToken: cancellationToken);

            // Expired messages in the retry queue are routed back to the main queue through the exchange.
            var retryArguments = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = Exchange,
                ["x-dead-letter-routing-key"] = queue,
            };

            await channel.QueueDeclareAsync(retry, durable: true, exclusive: false, autoDelete: false, arguments: retryArguments, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(retry, Exchange, retry, cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(deadLetter, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(deadLetter, Exchange, deadLetter, cancellationToken: cancellationToken);
        }
    }
}
