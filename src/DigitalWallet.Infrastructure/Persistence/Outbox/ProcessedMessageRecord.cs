namespace DigitalWallet.Infrastructure.Persistence.Outbox;

internal sealed class ProcessedMessageRecord
{
    public Guid MessageId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
