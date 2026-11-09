using System.Globalization;
using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Application.Movements;

public sealed record MovementCommand(Guid AccountId, decimal Amount);

public sealed record MovementResult(Guid TransactionId, string Type, decimal AccountBalance);

/// <summary>
/// Shared flow for deposits and withdrawals: load both sides, create the balanced transaction,
/// apply each entry to its account, store the transaction and commit everything at once.
/// </summary>
internal sealed class MovementUseCase(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<MovementResult> ExecuteAsync(
        MovementCommand command,
        Func<Guid, Guid, Money, DateTimeOffset, Transaction> createTransaction,
        CancellationToken cancellationToken)
    {
        var amount = ToMoney(command.Amount);

        var customer = await accounts.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new AccountNotFoundException(command.AccountId);

        var settlement = await accounts.GetByIdAsync(SettlementAccount.Id, cancellationToken)
            ?? throw new InvalidOperationException(
                "The settlement account does not exist. Run the startup initialization first.");

        var transaction = createTransaction(customer.Id, settlement.Id, amount, clock.UtcNow);

        foreach (var entry in transaction.Entries)
        {
            (entry.AccountId == customer.Id ? customer : settlement).ApplyEntry(entry);
        }

        await transactions.AddAsync(transaction, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return new MovementResult(transaction.Id, transaction.Type.ToString(), customer.Balance.Amount);
    }

    private static Money ToMoney(decimal amount)
    {
        // BRL has two decimal places. Extra precision is a malformed request, not a business rule.
        if (decimal.Round(amount, 2) != amount)
        {
            throw new RequestValidationException(
                "amount",
                string.Create(CultureInfo.InvariantCulture, $"The amount must have at most 2 decimal places. Received: {amount}."));
        }

        return Money.Brl(amount);
    }
}
