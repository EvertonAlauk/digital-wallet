using DigitalWallet.Application.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Infrastructure.Persistence.Idempotency;

internal sealed class IdempotencyRepository(WalletDbContext dbContext) : IIdempotencyRepository
{
    public async Task<bool> TryInsertAsync(IdempotencyEntry entry, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING is atomic: when two requests race, one inserts and the other affects zero rows.
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO idempotency_keys ("Key", "RequestHash", "StartedAt", "ExpiresAt", "IsCompleted")
            VALUES ({entry.Key}, {entry.RequestHash}, {entry.StartedAt}, {entry.ExpiresAt}, false)
            ON CONFLICT ("Key") DO NOTHING
            """, cancellationToken);

        return inserted == 1;
    }

    public async Task<IdempotencyEntry?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var record = await dbContext.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Key == key, cancellationToken);

        if (record is null)
        {
            return null;
        }

        StoredResponse? response = record.IsCompleted
            ? new StoredResponse(
                record.ResponseStatusCode!.Value,
                record.ResponseLocation,
                record.ResponseContentType,
                record.ResponseBody!)
            : null;

        return new IdempotencyEntry(record.Key, record.RequestHash, record.StartedAt, record.ExpiresAt, response);
    }

    public async Task<bool> DeleteIfExpiredAsync(string key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The expiry condition is in the same statement, so a fresh entry written by another request is never removed.
        var deleted = await dbContext.IdempotencyKeys
            .Where(row => row.Key == key && row.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken) =>
        dbContext.IdempotencyKeys
            .Where(row => row.Key == key)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.IsCompleted, true)
                .SetProperty(row => row.ResponseStatusCode, response.StatusCode)
                .SetProperty(row => row.ResponseLocation, response.Location)
                .SetProperty(row => row.ResponseContentType, response.ContentType)
                .SetProperty(row => row.ResponseBody, response.Body),
                cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        dbContext.IdempotencyKeys
            .Where(row => row.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
}
