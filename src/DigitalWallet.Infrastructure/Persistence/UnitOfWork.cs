using DigitalWallet.Application.Exceptions;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Infrastructure.Persistence;

internal sealed class UnitOfWork(WalletDbContext dbContext) : IUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            // SaveChanges runs in a single database transaction: all changes are persisted, or none are.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Only accounts carry the concurrency token, so the conflicting entity is an account.
            var account = exception.Entries.Select(entry => entry.Entity).OfType<Account>().FirstOrDefault();
            throw new ConcurrencyConflictException(nameof(Account), account?.Id ?? Guid.Empty);
        }
    }
}
