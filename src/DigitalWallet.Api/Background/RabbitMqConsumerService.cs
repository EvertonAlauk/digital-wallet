using System.Text;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Infrastructure.Messaging.RabbitMq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DigitalWallet.Api.Background;

/// <summary>
/// Consumes the main queue of every message type. A handler that succeeds acknowledges the message. A failure
/// republishes it to the retry queue (five retries, five seconds apart), and after the last retry to the
/// dead-letter topic, where it can be inspected and copied back to the main queue.
/// </summary>
internal sealed class RabbitMqConsumerService(
    RabbitMqConnection connection,
    RabbitMqPublishChannel publishChannel,
    IServiceScopeFactory scopes,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private const ushort Prefetch = 10;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilClosedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ consumer failed. Reconnecting in {Delay}.", ReconnectDelay);
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConsumeUntilClosedAsync(CancellationToken stoppingToken)
    {
        var rabbitConnection = await connection.GetAsync(stoppingToken);
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, Prefetch, false, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        foreach (var messageType in HandledMessageTypes())
        {
            var queue = QueueNames.For(messageType);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, queue, delivery, stoppingToken);

            await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        }

        using var registration = stoppingToken.Register(() => closed.TrySetCanceled());
        await closed.Task;
    }

    private IReadOnlyList<string> HandledMessageTypes()
    {
        using var scope = scopes.CreateScope();

        return scope.ServiceProvider
            .GetServices<IMessageHandler>()
            .Select(handler => handler.MessageType)
            .Distinct()
            .ToList();
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        string queue,
        BasicDeliverEventArgs delivery,
        CancellationToken stoppingToken)
    {
        var properties = delivery.BasicProperties;
        var attempt = RabbitMqHeaders.ReadAttempt(properties);

        try
        {
            var message = new OutboxMessage(
                Guid.Parse(properties.MessageId ?? throw new InvalidOperationException("The message has no id.")),
                properties.Type ?? throw new InvalidOperationException("The message has no type."),
                Encoding.UTF8.GetString(delivery.Body.Span),
                DateTimeOffset.UtcNow);

            await using var scope = scopes.CreateAsyncScope();
            var handlers = scope.ServiceProvider
                .GetServices<IMessageHandler>()
                .Where(handler => handler.MessageType == message.Type)
                .ToList();

            if (handlers.Count == 0)
            {
                throw new InvalidOperationException($"No handler is registered for {message.Type}.");
            }

            foreach (var handler in handlers)
            {
                await handler.HandleAsync(message, stoppingToken);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception exception)
        {
            var retry = RetryPolicy.ShouldRetry(attempt);
            var target = retry ? QueueNames.RetryFor(queue) : QueueNames.DeadLetterFor(queue);

            logger.LogWarning(
                exception,
                "Message {MessageId} failed on attempt {Attempt}. Sending it to {Target}.",
                properties.MessageId,
                attempt,
                target);

            var nextAttempt = retry ? attempt + 1 : attempt;
            await publishChannel.PublishAsync(
                target,
                RabbitMqHeaders.WithAttempt(properties, nextAttempt),
                delivery.Body,
                stoppingToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
    }
}
