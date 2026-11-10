using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;

namespace DigitalWallet.Application.Operations;

public sealed record AcceptTransferCommand(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);

public sealed class AcceptTransferUseCase(
    IAccountRepository accounts,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<OperationAccepted> ExecuteAsync(AcceptTransferCommand command, CancellationToken cancellationToken)
    {
        if (command.SourceAccountId == command.DestinationAccountId)
        {
            throw new InvalidTransactionException("Transferring to the same account is not allowed.");
        }

        var money = MoneyInput.ToBrl(command.Amount);

        _ = await accounts.GetByIdAsync(command.SourceAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(command.SourceAccountId);
        _ = await accounts.GetByIdAsync(command.DestinationAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(command.DestinationAccountId);

        var operation = Operation.Accept(
            Guid.NewGuid(),
            OperationKind.Transfer,
            command.SourceAccountId,
            command.DestinationAccountId,
            money.Amount,
            clock.UtcNow);

        await operations.AddAsync(operation, cancellationToken);
        outbox.Enqueue(OutboxMessage.Create(
            nameof(TransferRequested),
            new TransferRequested(operation.Id, command.SourceAccountId, command.DestinationAccountId, money.Amount),
            clock.UtcNow));

        await unitOfWork.CommitAsync(cancellationToken);

        return new OperationAccepted(operation.Id);
    }
}
