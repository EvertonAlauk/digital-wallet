using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Application.Operations.Handlers;

/// <summary>First pipeline step for a transfer. Advisory checks only; TransferCreated validates under lock.</summary>
public sealed class TransferRequestedHandler(
    IAccountRepository accounts,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IMessageHandler
{
    public string MessageType => nameof(TransferRequested);

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var requested = message.DeserializePayload<TransferRequested>();

        var operation = await operations.GetByIdAsync(requested.OperationId, cancellationToken)
            ?? throw new OperationNotFoundException(requested.OperationId);

        if (operation.Status != OperationStatus.Accepted)
        {
            return;
        }

        var source = await accounts.GetByIdAsync(requested.SourceAccountId, cancellationToken);
        var destination = await accounts.GetByIdAsync(requested.DestinationAccountId, cancellationToken);

        var rejection = (source, destination) switch
        {
            (null, _) => "The source account was not found.",
            (_, null) => "The destination account was not found.",
            ({ Status: AccountStatus.Blocked }, _) => "The source account is blocked.",
            (_, { Status: AccountStatus.Blocked }) => "The destination account is blocked.",
            ({ Balance.Amount: var balance }, _) when balance < requested.Amount => "Insufficient funds.",
            _ => null,
        };

        if (rejection is null)
        {
            outbox.Enqueue(OutboxMessage.Create(
                nameof(TransferCreated),
                new TransferCreated(requested.OperationId, requested.SourceAccountId, requested.DestinationAccountId, requested.Amount),
                clock.UtcNow));
        }
        else
        {
            operation.Reject(rejection, clock.UtcNow);
            await operations.UpdateAsync(operation, cancellationToken);
            outbox.Enqueue(OutboxMessage.Create(
                nameof(TransferRejected),
                new TransferRejected(requested.OperationId, requested.SourceAccountId, rejection),
                clock.UtcNow));
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
