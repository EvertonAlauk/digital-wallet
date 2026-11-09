using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Domain.Entities;

/// <summary>
/// Immutable ledger entry. Amount is signed: positive is a credit, negative is a debit.
/// There is no mutating operation: corrections are made by reversal.
/// </summary>
public sealed record LedgerEntry
{
    public LedgerEntry(Guid accountId, Money amount, Guid transactionId)
    {
        if (amount.IsZero)
        {
            throw new InvalidAmountException("A ledger entry cannot have a zero amount.");
        }

        AccountId = accountId;
        Amount = amount;
        TransactionId = transactionId;
    }

    // Used by EF Core to materialize entities. Values are set by the persistence mapping.
    private LedgerEntry()
    {
        Amount = Money.Zero();
    }

    public Guid AccountId { get; }

    public Money Amount { get; }

    public Guid TransactionId { get; }
}
