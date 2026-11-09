using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Ports;

/// <summary>
/// Access to transactions and their ledger entries. Transactions are only added; the ledger is append-only.
/// </summary>
public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);
}
