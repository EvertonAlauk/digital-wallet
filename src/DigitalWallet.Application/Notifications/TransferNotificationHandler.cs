using DigitalWallet.Application.Outbox;
using DigitalWallet.Domain.Events;

namespace DigitalWallet.Application.Notifications;

/// <summary>
/// Example consumer: sends a notification for every completed transfer.
/// Idempotent: a message is marked processed before sending, and the mark is removed if sending fails.
/// A duplicate delivery of a message that was already sent is ignored.
/// </summary>
public sealed class TransferNotificationHandler(
    IProcessedMessageStore processedMessages,
    INotificationSender sender) : IMessageHandler
{
    public string MessageType => nameof(TransferCompleted);

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!await processedMessages.TryMarkAsync(message.Id, cancellationToken))
        {
            return;
        }

        try
        {
            var transfer = message.DeserializePayload<TransferCompleted>();
            var text = $"Transfer {transfer.TransactionId}: {transfer.Amount:0.00} {transfer.Currency} " +
                       $"from {transfer.SourceAccountId} to {transfer.DestinationAccountId}.";

            await sender.SendAsync(new Notification(message.Id, text), cancellationToken);
        }
        catch
        {
            await processedMessages.UnmarkAsync(message.Id, cancellationToken);
            throw;
        }
    }
}
