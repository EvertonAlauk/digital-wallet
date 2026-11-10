namespace DigitalWallet.Application.Outbox;

/// <summary>
/// Records an event to publish. The event is stored in the same database commit as the changes that produced it:
/// if the unit of work does not commit, the event is never stored.
/// </summary>
public interface IOutbox
{
    void Enqueue(OutboxMessage message);
}
