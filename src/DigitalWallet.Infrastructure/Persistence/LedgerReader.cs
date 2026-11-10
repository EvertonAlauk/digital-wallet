using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Infrastructure.Persistence;

internal sealed class LedgerReader(WalletDbContext dbContext) : ILedgerReader
{
    public async Task<IReadOnlyList<LedgerRecord>> ReadAccountEntriesAsync(
        Guid accountId,
        long? beforeSequence,
        int limit,
        LedgerFilter filter,
        CancellationToken cancellationToken)
    {
        var query = from entry in dbContext.LedgerEntries
                    join transaction in dbContext.Transactions on entry.TransactionId equals transaction.Id
                    where entry.AccountId == accountId
                    select new
                    {
                        Sequence = EF.Property<long>(entry, "Id"),
                        entry.TransactionId,
                        transaction.Type,
                        Amount = entry.Amount.Amount,
                        Currency = entry.Amount.Currency,
                        transaction.CreatedAt,
                    };

        if (beforeSequence is { } before)
        {
            query = query.Where(row => row.Sequence < before);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(row => row.Type == type);
        }

        if (filter.From is { } from)
        {
            query = query.Where(row => row.CreatedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(row => row.CreatedAt < to);
        }

        var rows = await query
            .OrderByDescending(row => row.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new LedgerRecord(
                row.Sequence,
                row.TransactionId,
                row.Type,
                new Money(row.Amount, row.Currency),
                row.CreatedAt))
            .ToList();
    }
}
