using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Ports;

/// <summary>
/// Access to accounts. Changes are staged and only persisted by <see cref="IUnitOfWork.CommitAsync"/>.
/// </summary>
public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(Account account, CancellationToken cancellationToken);
}
