using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Movements;

public sealed class WithdrawalUseCase(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private readonly MovementUseCase _movement = new(accounts, transactions, unitOfWork, clock);

    public Task<MovementResult> ExecuteAsync(MovementCommand command, CancellationToken cancellationToken) =>
        _movement.ExecuteAsync(
            command,
            (customerId, settlementId, amount, now) =>
                Transaction.Withdrawal(Guid.NewGuid(), customerId, settlementId, amount, now),
            cancellationToken);
}
