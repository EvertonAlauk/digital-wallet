namespace DigitalWallet.Application.Notifications;

/// <summary>Remembers which messages a consumer has handled, so a redelivered message is ignored.</summary>
public interface IProcessedMessageStore
{
    /// <summary>Marks the message as processed. Returns false when it was already marked (a duplicate delivery).</summary>
    Task<bool> TryMarkAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Removes the mark, so a failed attempt can be processed again.</summary>
    Task UnmarkAsync(Guid messageId, CancellationToken cancellationToken);
}
