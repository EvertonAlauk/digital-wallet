using DigitalWallet.Application.Exceptions;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DigitalWallet.Infrastructure.Persistence;

internal sealed class UnitOfWork(WalletDbContext dbContext) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        // Row locks from SELECT ... FOR UPDATE live until this transaction ends.
        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Without an explicit transaction, SaveChanges opens its own. With one, the changes join it.
            await dbContext.SaveChangesAsync(cancellationToken);

            if (_transaction is not null)
            {
                await _transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Only accounts carry the concurrency token, so the conflicting entity is an account.
            var account = exception.Entries.Select(entry => entry.Entity).OfType<Account>().FirstOrDefault();
            throw new ConcurrencyConflictException(nameof(Account), account?.Id ?? Guid.Empty);
        }
    }
}
