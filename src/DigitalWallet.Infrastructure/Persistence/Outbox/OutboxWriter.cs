using DigitalWallet.Application.Outbox;

namespace DigitalWallet.Infrastructure.Persistence.Outbox;

/// <summary>
/// Stages outbox messages on the same <see cref="WalletDbContext"/> as the unit of work. They are written by the
/// same SaveChanges as the ledger and account changes, so the event and the change commit together or not at all.
/// </summary>
internal sealed class OutboxWriter(WalletDbContext dbContext) : IOutbox
{
    public void Enqueue(OutboxMessage message) =>
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = message.Id,
            Type = message.Type,
            Payload = message.Payload,
            OccurredAt = message.OccurredAt,
        });
}
