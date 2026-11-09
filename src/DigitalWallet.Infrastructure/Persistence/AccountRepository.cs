using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;

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
}
