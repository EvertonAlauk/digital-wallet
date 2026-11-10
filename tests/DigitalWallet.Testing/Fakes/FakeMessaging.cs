using DigitalWallet.Application.Outbox;

namespace DigitalWallet.Testing.Fakes;

/// <summary>
/// Stands in for the message broker. Records every message it delivers. <see cref="FailWhen"/> simulates an
/// unavailable broker: the publish throws, and the message is not recorded as delivered.
/// </summary>
public sealed class InMemoryMessagePublisher : IMessagePublisher
{
    private readonly List<OutboxMessage> _delivered = new();
    private readonly object _gate = new();

    /// <summary>Decides, per message, whether the publish should fail. Defaults to never failing.</summary>
    public Func<OutboxMessage, bool> FailWhen { get; set; } = _ => false;

    public int PublishCalls { get; private set; }

    public IReadOnlyList<OutboxMessage> Delivered
    {
        get
        {
            lock (_gate)
            {
                return _delivered.ToList();
            }
        }
    }

    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            PublishCalls++;

            if (FailWhen(message))
            {
                throw new InvalidOperationException("The message broker is unavailable.");
            }

            _delivered.Add(message);
        }

        return Task.CompletedTask;
    }
}
