using DigitalWallet.Application.Movements;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Exceptions;

namespace DigitalWallet.Application.Transfers;

public sealed record TransferCommand(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);

public sealed record TransferResult(Guid TransactionId, decimal SourceBalance, decimal DestinationBalance);

/// <summary>
/// Moves money between two accounts in one database transaction.
/// Concurrency: both accounts are locked with SELECT ... FOR UPDATE, in ascending id order, and balances are
/// read after the lock. This is pessimistic locking, chosen in ADR 0005.
/// </summary>
public sealed class TransferUseCase(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<TransferResult> ExecuteAsync(TransferCommand command, CancellationToken cancellationToken)
    {
        // Checked before locking: locking the same account twice would wait on itself.
        if (command.SourceAccountId == command.DestinationAccountId)
        {
            throw new InvalidTransactionException("Transferring to the same account is not allowed.");
        }

        var amount = MovementUseCase.ToMoney(command.Amount);

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        var locked = await accounts.GetByIdsForUpdateAsync(
            [command.SourceAccountId, command.DestinationAccountId],
            cancellationToken);

        var source = locked[command.SourceAccountId];
        var destination = locked[command.DestinationAccountId];

        // Business rules run under the lock. A failure leaves nothing committed; the transaction rolls back.
        var transfer = Transaction.Transfer(Guid.NewGuid(), source.Id, destination.Id, amount, clock.UtcNow);

        foreach (var entry in transfer.Entries)
        {
            (entry.AccountId == source.Id ? source : destination).ApplyEntry(entry);
        }

        await transactions.AddAsync(transfer, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return new TransferResult(transfer.Id, source.Balance.Amount, destination.Balance.Amount);
    }
}
