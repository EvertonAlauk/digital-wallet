namespace DigitalWallet.Infrastructure.Persistence.Outbox;

/// <summary>Database row of an outbox message. <see cref="Sequence"/> keeps the commit order of the messages.</summary>
internal sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }

    public long Sequence { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}
