using DigitalWallet.Application.Outbox;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Infrastructure.Persistence.Outbox;

internal sealed class OutboxStore(WalletDbContext dbContext) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int limit, CancellationToken cancellationToken)
    {
        var records = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return records
            .Select(record => new OutboxMessage(record.Id, record.Type, record.Payload, record.OccurredAt))
            .ToList();
    }

    public Task MarkPublishedAsync(Guid messageId, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        dbContext.OutboxMessages
            .Where(message => message.Id == messageId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.PublishedAt, publishedAt), cancellationToken);
}
