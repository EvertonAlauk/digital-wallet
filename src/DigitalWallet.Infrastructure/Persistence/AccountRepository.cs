using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DigitalWallet.Infrastructure.Persistence;

internal sealed class AccountRepository(WalletDbContext dbContext) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Accounts.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    public Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        dbContext.Accounts.Add(account);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Account>> ListByOwnerAsync(
        Guid ownerId,
        AccountStatus? status,
        int? limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = OwnedAccounts(ownerId, status)
            .OrderByDescending(account => account.CreatedAt)
            .ThenBy(account => account.Id)
            .Skip(offset);

        if (limit is { } take)
        {
            query = query.Take(take);
        }

        return await query.AsNoTracking().ToListAsync(cancellationToken);
    }

    public Task<int> CountByOwnerAsync(Guid ownerId, AccountStatus? status, CancellationToken cancellationToken) =>
        OwnedAccounts(ownerId, status).CountAsync(cancellationToken);

    private IQueryable<Account> OwnedAccounts(Guid ownerId, AccountStatus? status)
    {
        var query = dbContext.Accounts
            .Where(account => account.OwnerId == ownerId && account.Kind == AccountKind.Customer);

        return status is { } wanted ? query.Where(account => account.Status == wanted) : query;
    }

    public async Task<IReadOnlyDictionary<Guid, Account>> GetByIdsForUpdateAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var ordered = ids.Distinct().OrderBy(id => id).ToArray();

        // PostgreSQL takes the row locks in ORDER BY order, so every operation locks accounts in the same order.
        var rows = await dbContext.Accounts
            .FromSqlRaw(
                "SELECT * FROM accounts WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE",
                new NpgsqlParameter("ids", ordered))
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(account => account.Id);

        foreach (var id in ordered)
        {
            if (!byId.ContainsKey(id))
            {
                throw new AccountNotFoundException(id);
            }
        }

        return byId;
    }
}
