using DigitalWallet.Application.Common;
using DigitalWallet.Application.Messaging;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Events;
using DigitalWallet.Domain.Exceptions;

namespace DigitalWallet.Application.Operations.Handlers;

/// <summary>
/// The step that moves money for a transfer. Locks both accounts in ascending id order (ADR 0005), then reads the
/// operation. On success it also writes TransferCompleted to the outbox, for notifications (ADR 0007).
/// </summary>
public sealed class TransferCreatedHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IOperationRepository operations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IMessageHandler
{
    public string MessageType => nameof(TransferCreated);

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var created = message.DeserializePayload<TransferCreated>();

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        var locked = await accounts.GetByIdsForUpdateAsync(
            [created.SourceAccountId, created.DestinationAccountId],
            cancellationToken);

        var operation = await operations.GetByIdAsync(created.OperationId, cancellationToken)
            ?? throw new OperationNotFoundException(created.OperationId);

        if (operation.Status != OperationStatus.Accepted)
        {
            await unitOfWork.CommitAsync(cancellationToken);
            return;
        }

        var source = locked[created.SourceAccountId];
        var destination = locked[created.DestinationAccountId];

        try
        {
            var transfer = Transaction.Transfer(
                Guid.NewGuid(),
                source.Id,
                destination.Id,
                Domain.ValueObjects.Money.Brl(created.Amount),
                clock.UtcNow);

            foreach (var entry in transfer.Entries)
            {
                (entry.AccountId == source.Id ? source : destination).EnsureCanApply(entry);
            }

            foreach (var entry in transfer.Entries)
            {
                (entry.AccountId == source.Id ? source : destination).ApplyEntry(entry);
            }

            await transactions.AddAsync(transfer, cancellationToken);
            operation.Complete(transfer.Id, clock.UtcNow);

            var completed = new TransferCompleted(
                transfer.Id,
                source.Id,
                destination.Id,
                created.Amount,
                "BRL",
                clock.UtcNow);

            outbox.Enqueue(OutboxMessage.Create(nameof(TransferCompleted), completed, completed.OccurredAt));
        }
        catch (DomainException exception)
        {
            operation.Reject(exception.Message, clock.UtcNow);
            outbox.Enqueue(OutboxMessage.Create(
                nameof(TransferRejected),
                new TransferRejected(created.OperationId, created.SourceAccountId, exception.Message),
                clock.UtcNow));
        }

        await operations.UpdateAsync(operation, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
