using DigitalWallet.Application.Operations;
using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Infrastructure.Persistence.Operations;

internal sealed class OperationRepository(WalletDbContext dbContext) : IOperationRepository
{
    public Task AddAsync(Operation operation, CancellationToken cancellationToken)
    {
        dbContext.Operations.Add(operation);
        return Task.CompletedTask;
    }

    public Task<Operation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Operations.FirstOrDefaultAsync(operation => operation.Id == id, cancellationToken);

    // The operation is tracked by this DbContext, so SaveChanges writes the change in the same commit.
    public Task UpdateAsync(Operation operation, CancellationToken cancellationToken) => Task.CompletedTask;
}
