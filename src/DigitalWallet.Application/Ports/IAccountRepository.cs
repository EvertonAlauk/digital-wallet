using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Ports;

/// <summary>
/// Access to accounts. Changes are staged and only persisted by <see cref="IUnitOfWork.CommitAsync"/>.
/// </summary>
public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(Account account, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the given accounts (SELECT ... FOR UPDATE) and returns them with their latest committed state.
    /// Locks are acquired in ascending id order, so two operations locking the same accounts cannot deadlock.
    /// Must be the first access to these accounts in the unit of work. Throws
    /// <see cref="Exceptions.AccountNotFoundException"/> when any account does not exist.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Account>> GetByIdsForUpdateAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
}
