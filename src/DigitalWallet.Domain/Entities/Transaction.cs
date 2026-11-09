using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Domain.Entities;

/// <summary>
/// Accounting movement made of double-entry ledger entries.
/// The entries sum to zero per currency and cannot be changed after creation.
/// </summary>
public sealed class Transaction
{
    private readonly List<LedgerEntry> _entries;

    private Transaction(Guid id, TransactionType type, DateTimeOffset createdAt, List<LedgerEntry> entries)
    {
        Id = id;
        Type = type;
        CreatedAt = createdAt;
        Status = TransactionStatus.Completed;
        _entries = entries;
    }

    // Used by EF Core to materialize entities. Values are set by the persistence mapping.
    private Transaction()
    {
        _entries = new List<LedgerEntry>();
    }

    public Guid Id { get; }

    public TransactionType Type { get; }

    public TransactionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <summary>
    /// Restores a transaction from persisted state. Does not re-validate the entries, because they
    /// were validated when the transaction was created.
    /// </summary>
    public static Transaction Rehydrate(
        Guid id,
        TransactionType type,
        TransactionStatus status,
        DateTimeOffset createdAt,
        IEnumerable<LedgerEntry> entries)
    {
        var transaction = new Transaction(id, type, createdAt, entries.ToList())
        {
            Status = status,
        };

        return transaction;
    }

    /// <summary>
    /// Creates a transaction from legs (account, signed amount).
    /// Requires at least two legs, non-zero amounts, the same currency and a zero sum.
    /// </summary>
    public static Transaction Create(
        Guid id,
        TransactionType type,
        DateTimeOffset createdAt,
        IEnumerable<(Guid AccountId, Money Amount)> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);

        var legs = movements.ToList();

        if (legs.Count < 2)
        {
            throw new InvalidTransactionException(
                "A transaction needs at least two ledger entries (double entry).");
        }

        var currency = legs[0].Amount.Currency;
        foreach (var leg in legs)
        {
            if (leg.Amount.Currency != currency)
            {
                throw new CurrencyMismatchException(currency, leg.Amount.Currency);
            }
        }

        var netSum = legs.Sum(leg => leg.Amount.Amount);
        if (netSum != 0m)
        {
            throw new UnbalancedTransactionException(netSum);
        }

        // The LedgerEntry constructor rejects zero amounts.
        var entries = legs
            .Select(leg => new LedgerEntry(leg.AccountId, leg.Amount, id))
            .ToList();

        return new Transaction(id, type, createdAt, entries);
    }

    /// <summary>Transfer between two accounts: a debit on the source and a credit on the destination, for the same amount.</summary>
    public static Transaction Transfer(
        Guid id,
        Guid fromAccountId,
        Guid toAccountId,
        Money amount,
        DateTimeOffset createdAt)
    {
        amount.EnsurePositive();

        if (fromAccountId == toAccountId)
        {
            throw new InvalidTransactionException("Transferring to the same account is not allowed.");
        }

        return Create(
            id,
            TransactionType.Transfer,
            createdAt,
            new[]
            {
                (fromAccountId, amount.Negate()),
                (toAccountId, amount),
            });
    }

    /// <summary>
    /// Deposit: money enters the customer account and leaves the settlement account.
    /// </summary>
    public static Transaction Deposit(
        Guid id,
        Guid customerAccountId,
        Guid settlementAccountId,
        Money amount,
        DateTimeOffset createdAt)
    {
        amount.EnsurePositive();

        return Create(
            id,
            TransactionType.Deposit,
            createdAt,
            new[]
            {
                (customerAccountId, amount),
                (settlementAccountId, amount.Negate()),
            });
    }

    /// <summary>
    /// Withdrawal: money leaves the customer account and returns to the settlement account.
    /// </summary>
    public static Transaction Withdrawal(
        Guid id,
        Guid customerAccountId,
        Guid settlementAccountId,
        Money amount,
        DateTimeOffset createdAt)
    {
        amount.EnsurePositive();

        return Create(
            id,
            TransactionType.Withdrawal,
            createdAt,
            new[]
            {
                (customerAccountId, amount.Negate()),
                (settlementAccountId, amount),
            });
    }

    /// <summary>
    /// Marks the transaction as reversed. The reversal itself is a new transaction with the entries inverted;
    /// the original entries remain intact.
    /// </summary>
    public void MarkAsReversed()
    {
        if (Type == TransactionType.Reversal)
        {
            throw new InvalidTransactionException("A reversal cannot be reversed.");
        }

        if (Status != TransactionStatus.Completed)
        {
            throw new InvalidTransactionException(
                $"Only completed transactions can be reversed. Current status: {Status}.");
        }

        Status = TransactionStatus.Reversed;
    }
}
