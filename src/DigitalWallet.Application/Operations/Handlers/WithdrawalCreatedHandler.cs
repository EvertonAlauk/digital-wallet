using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Application.Operations.Handlers;

/// <summary>The step that moves money for a withdrawal. Same locking and validation rules as DepositCreatedHandler.</summary>
public sealed class WithdrawalCreatedHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IMessageHandler
{
    public string MessageType => nameof(WithdrawalCreated);

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var created = message.DeserializePayload<WithdrawalCreated>();

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        var locked = await accounts.GetByIdsForUpdateAsync(
            [created.AccountId, SettlementAccount.Id],
            cancellationToken);

        var operation = await operations.GetByIdAsync(created.OperationId, cancellationToken)
            ?? throw new OperationNotFoundException(created.OperationId);

        if (operation.Status != OperationStatus.Accepted)
        {
            await unitOfWork.CommitAsync(cancellationToken);
            return;
        }

        var customer = locked[created.AccountId];
        var settlement = locked[SettlementAccount.Id];

        try
        {
            var transaction = Transaction.Withdrawal(
                Guid.NewGuid(),
                customer.Id,
                settlement.Id,
                Money.Brl(created.Amount),
                clock.UtcNow);

            foreach (var entry in transaction.Entries)
            {
                (entry.AccountId == customer.Id ? customer : settlement).EnsureCanApply(entry);
            }

            foreach (var entry in transaction.Entries)
            {
                (entry.AccountId == customer.Id ? customer : settlement).ApplyEntry(entry);
            }

            await transactions.AddAsync(transaction, cancellationToken);
            operation.Complete(transaction.Id, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            operation.Reject(exception.Message, clock.UtcNow);
            outbox.Enqueue(OutboxMessage.Create(
                nameof(WithdrawalRejected),
                new WithdrawalRejected(created.OperationId, created.AccountId, exception.Message),
                clock.UtcNow));
        }

        await operations.UpdateAsync(operation, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
