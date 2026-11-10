using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Application.Operations;

public sealed record AcceptWithdrawalCommand(Guid AccountId, decimal Amount);

public sealed class AcceptWithdrawalUseCase(
    IAccountRepository accounts,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<OperationAccepted> ExecuteAsync(AcceptWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var money = MoneyInput.ToBrl(command.Amount);

        _ = await accounts.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new AccountNotFoundException(command.AccountId);

        var operation = Operation.Accept(
            Guid.NewGuid(),
            OperationKind.Withdrawal,
            command.AccountId,
            destinationAccountId: null,
            money.Amount,
            clock.UtcNow);

        await operations.AddAsync(operation, cancellationToken);
        outbox.Enqueue(OutboxMessage.Create(
            nameof(WithdrawalRequested),
            new WithdrawalRequested(operation.Id, command.AccountId, money.Amount),
            clock.UtcNow));

        await unitOfWork.CommitAsync(cancellationToken);

        return new OperationAccepted(operation.Id);
    }
}
