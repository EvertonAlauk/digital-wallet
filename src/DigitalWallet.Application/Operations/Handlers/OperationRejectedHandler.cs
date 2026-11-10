using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Notifications;
using DigitalWallet.Application.Outbox;

namespace DigitalWallet.Application.Operations.Handlers;

/// <summary>
/// Consumer for the "Rejected" messages of one operation kind. Tells the client-facing side that a request failed a
/// business rule. Idempotent in the same way as TransferNotificationHandler.
/// </summary>
public sealed class OperationRejectedHandler(
    string messageType,
    IProcessedMessageStore processedMessages,
    INotificationSender sender) : IMessageHandler
{
    public string MessageType { get; } = messageType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!await processedMessages.TryMarkAsync(message.Id, cancellationToken))
        {
            return;
        }

        try
        {
            var rejected = message.DeserializePayload<RejectedPayload>();
            var text = $"Operation {rejected.OperationId} was rejected: {rejected.Reason}";

            await sender.SendAsync(new Notification(message.Id, text), cancellationToken);
        }
        catch
        {
            await processedMessages.UnmarkAsync(message.Id, cancellationToken);
            throw;
        }
    }
}
