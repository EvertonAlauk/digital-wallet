using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Application.Ports;

/// <summary>
/// Access to accounts. Changes are staged and only persisted by <see cref="IUnitOfWork.CommitAsync"/>.
/// </summary>
public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(Account account, CancellationToken cancellationToken);

    /// <summary>
    /// Customer accounts of one owner, newest first. Filters by status when given, and returns at most
    /// <paramref name="limit"/> accounts when given (all of them when null).
    /// </summary>
    Task<IReadOnlyList<Account>> ListByOwnerAsync(
        Guid ownerId,
        AccountStatus? status,
        int? limit,
        int offset,
        CancellationToken cancellationToken);

    /// <summary>How many customer accounts of one owner match the status filter (all when null).</summary>
    Task<int> CountByOwnerAsync(Guid ownerId, AccountStatus? status, CancellationToken cancellationToken);

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
