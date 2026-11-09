using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Infrastructure.Persistence;

internal sealed class TransactionRepository(WalletDbContext dbContext) : ITransactionRepository
{
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Transactions
            .Include("_entries")
            .FirstOrDefaultAsync(transaction => transaction.Id == id, cancellationToken);

    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        dbContext.Transactions.Add(transaction);
        return Task.CompletedTask;
    }
}
