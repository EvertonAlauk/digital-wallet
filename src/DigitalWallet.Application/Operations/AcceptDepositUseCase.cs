using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;

namespace DigitalWallet.Application.Operations;

public sealed record AcceptDepositCommand(Guid AccountId, decimal Amount);

/// <summary>Returned when a request is accepted. The outcome is read later from GET /operations/{id}.</summary>
public sealed record OperationAccepted(Guid OperationId);

/// <summary>
/// Accepts a deposit: records the operation and the DepositRequested event in one commit, then returns. Nothing moves
/// until the background pipeline processes the event.
/// </summary>
public sealed class AcceptDepositUseCase(
    IAccountRepository accounts,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<OperationAccepted> ExecuteAsync(AcceptDepositCommand command, CancellationToken cancellationToken)
    {
        var money = MoneyInput.ToBrl(command.Amount);

        // Cheap, synchronous checks give the client an immediate answer. Balance rules are checked by the pipeline.
        _ = await accounts.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new AccountNotFoundException(command.AccountId);

        var operation = Operation.Accept(
            Guid.NewGuid(),
            OperationKind.Deposit,
            command.AccountId,
            destinationAccountId: null,
            money.Amount,
            clock.UtcNow);

        await operations.AddAsync(operation, cancellationToken);
        outbox.Enqueue(OutboxMessage.Create(
            nameof(DepositRequested),
            new DepositRequested(operation.Id, command.AccountId, money.Amount),
            clock.UtcNow));

        await unitOfWork.CommitAsync(cancellationToken);

        return new OperationAccepted(operation.Id);
    }
}
