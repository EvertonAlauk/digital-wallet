using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Operations;

/// <summary>
/// Operations are written in the same unit of work as the change they describe, so the outcome and the money
/// movement commit together. Concurrency is handled by the status rule: only an Accepted operation can change.
/// </summary>
public interface IOperationRepository
{
    Task AddAsync(Operation operation, CancellationToken cancellationToken);

    Task<Operation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(Operation operation, CancellationToken cancellationToken);
}
