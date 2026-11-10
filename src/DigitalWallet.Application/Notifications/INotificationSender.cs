namespace DigitalWallet.Application.Notifications;

/// <summary>Sends a notification to the outside world (email, push, webhook...).</summary>
public interface INotificationSender
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record Notification(Guid MessageId, string Text);
