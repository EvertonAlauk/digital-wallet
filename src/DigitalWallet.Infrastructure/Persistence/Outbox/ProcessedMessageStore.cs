using DigitalWallet.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Infrastructure.Persistence.Outbox;

internal sealed class ProcessedMessageStore(WalletDbContext dbContext) : IProcessedMessageStore
{
    public async Task<bool> TryMarkAsync(Guid messageId, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING: when two deliveries race, exactly one inserts the row.
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processed_messages ("MessageId", "ProcessedAt")
            VALUES ({messageId}, {DateTimeOffset.UtcNow})
            ON CONFLICT ("MessageId") DO NOTHING
            """, cancellationToken);

        return inserted == 1;
    }

    public Task UnmarkAsync(Guid messageId, CancellationToken cancellationToken) =>
        dbContext.ProcessedMessages
            .Where(message => message.MessageId == messageId)
            .ExecuteDeleteAsync(cancellationToken);
}
