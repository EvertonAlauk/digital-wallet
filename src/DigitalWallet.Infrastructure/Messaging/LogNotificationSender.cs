using DigitalWallet.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace DigitalWallet.Infrastructure.Messaging;

/// <summary>Stand-in notification channel: writes the notification to the log instead of sending it.</summary>
internal sealed class LogNotificationSender(ILogger<LogNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Notification for message {MessageId}: {Text}",
            notification.MessageId,
            notification.Text);

        return Task.CompletedTask;
    }
}
