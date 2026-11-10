using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Application.Operations.Handlers;

/// <summary>First pipeline step for a withdrawal. Advisory checks only; WithdrawalCreated validates under lock.</summary>
public sealed class WithdrawalRequestedHandler(
    IAccountRepository accounts,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IMessageHandler
{
    public string MessageType => nameof(WithdrawalRequested);

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var requested = message.DeserializePayload<WithdrawalRequested>();

        var operation = await operations.GetByIdAsync(requested.OperationId, cancellationToken)
            ?? throw new OperationNotFoundException(requested.OperationId);

        if (operation.Status != OperationStatus.Accepted)
        {
            return;
        }

        var account = await accounts.GetByIdAsync(requested.AccountId, cancellationToken);
        var rejection = account switch
        {
            null => "The account was not found.",
            { Status: AccountStatus.Blocked } => "The account is blocked.",
            { Balance.Amount: var balance } when balance < requested.Amount => "Insufficient funds.",
            _ => null,
        };

        if (rejection is null)
        {
            outbox.Enqueue(OutboxMessage.Create(
                nameof(WithdrawalCreated),
                new WithdrawalCreated(requested.OperationId, requested.AccountId, requested.Amount),
                clock.UtcNow));
        }
        else
        {
            operation.Reject(rejection, clock.UtcNow);
            await operations.UpdateAsync(operation, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
