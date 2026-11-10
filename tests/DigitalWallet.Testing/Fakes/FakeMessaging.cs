using DigitalWallet.Application.Notifications;
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

/// <summary>Records notifications instead of sending them. <see cref="FailNext"/> makes the next sends throw.</summary>
public sealed class InMemoryNotificationSender : INotificationSender
{
    private readonly List<Notification> _sent = new();
    private readonly object _gate = new();

    public int FailNext { get; set; }

    public IReadOnlyList<Notification> Sent
    {
        get
        {
            lock (_gate)
            {
                return _sent.ToList();
            }
        }
    }

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (FailNext > 0)
            {
                FailNext--;
                throw new InvalidOperationException("The notification service is unavailable.");
            }

            _sent.Add(notification);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Set of processed message ids. Thread-safe.</summary>
public sealed class InMemoryProcessedMessageStore : IProcessedMessageStore
{
    private readonly HashSet<Guid> _processed = new();
    private readonly object _gate = new();

    public Task<bool> TryMarkAsync(Guid messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_processed.Add(messageId));
        }
    }

    public Task UnmarkAsync(Guid messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _processed.Remove(messageId);
        }

        return Task.CompletedTask;
    }
}
