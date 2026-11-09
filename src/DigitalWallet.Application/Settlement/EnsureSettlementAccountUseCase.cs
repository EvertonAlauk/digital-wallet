using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Settlement;

/// <summary>Creates the settlement account if it does not exist. Safe to run on every startup.</summary>
public sealed class EnsureSettlementAccountUseCase(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (await accounts.GetByIdAsync(SettlementAccount.Id, cancellationToken) is not null)
        {
            return;
        }

        await accounts.AddAsync(Account.OpenSettlement(SettlementAccount.Id, clock.UtcNow), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
