using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Application.Operations.Handlers;

/// <summary>
/// First pipeline step for a deposit: checks the account and either requests the credit (DepositCreated) or rejects
/// the operation. The checks here are advisory: the balance can change before DepositCreated is handled, so that
/// handler validates again under lock.
/// </summary>
public sealed class DepositRequestedHandler(
    IAccountRepository accounts,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IMessageHandler
{
    public string MessageType => nameof(DepositRequested);

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var requested = message.DeserializePayload<DepositRequested>();

        var operation = await operations.GetByIdAsync(requested.OperationId, cancellationToken)
            ?? throw new OperationNotFoundException(requested.OperationId);

        // Duplicate delivery of a step that already moved on: nothing to do.
        if (operation.Status != OperationStatus.Accepted)
        {
            return;
        }

        var account = await accounts.GetByIdAsync(requested.AccountId, cancellationToken);
        var rejection = account switch
        {
            null => "The account was not found.",
            { Status: AccountStatus.Blocked } => "The account is blocked.",
            _ => null,
        };

        if (rejection is null)
        {
            outbox.Enqueue(OutboxMessage.Create(
                nameof(DepositCreated),
                new DepositCreated(requested.OperationId, requested.AccountId, requested.Amount),
                clock.UtcNow));
        }
        else
        {
            // The rejection is recorded on the operation, which the client reads through GET /operations/{id}.
            operation.Reject(rejection, clock.UtcNow);
            await operations.UpdateAsync(operation, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
