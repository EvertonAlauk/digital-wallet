using RabbitMQ.Client;

namespace DigitalWallet.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// The channel used to publish. Publisher confirms are on, so a publish returns only after the broker has accepted
/// the message. A channel is not thread-safe, so publishes are serialized. Declares the topology when it opens.
/// </summary>
public sealed class RabbitMqPublishChannel(RabbitMqConnection connection) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await OpenAsync(cancellationToken);
            await channel.BasicPublishAsync(
                RabbitMqTopology.Exchange,
                routingKey,
                mandatory: false,
                properties,
                body,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> OpenAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        var rabbitConnection = await connection.GetAsync(cancellationToken);
        var channel = await rabbitConnection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

        _channel = channel;
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
    }
}
