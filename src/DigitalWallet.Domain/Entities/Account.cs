using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Domain.Entities;

/// <summary>
/// Account of a holder. The balance is a cache of the ledger entries and changes only via <see cref="ApplyEntry"/>.
/// </summary>
public sealed class Account
{
    private Account(Guid id, Guid ownerId, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        CreatedAt = createdAt;
        Balance = Money.Zero();
        Status = AccountStatus.Active;
        Version = 0;
    }

    public Guid Id { get; }

    public Guid OwnerId { get; }

    public Money Balance { get; private set; }

    public AccountStatus Status { get; private set; }

    /// <summary>Optimistic concurrency token. Incremented on every movement.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static Account Open(Guid id, Guid ownerId, DateTimeOffset createdAt) =>
        new(id, ownerId, createdAt);

    public void Block() => Status = AccountStatus.Blocked;

    public void Unblock() => Status = AccountStatus.Active;

    /// <summary>
    /// Applies a ledger entry to the balance. A blocked account cannot move funds, and the balance never becomes negative.
    /// </summary>
    public void ApplyEntry(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.AccountId != Id)
        {
            throw new InvalidTransactionException(
                $"The ledger entry belongs to account {entry.AccountId}, not to account {Id}.");
        }

        if (Status == AccountStatus.Blocked)
        {
            throw new AccountBlockedException(Id);
        }

        var newBalance = Balance.Add(entry.Amount);

        if (newBalance.Amount < 0m)
        {
            throw new InsufficientFundsException(Id, Balance.Amount, -entry.Amount.Amount);
        }

        Balance = newBalance;
        Version++;
    }
}
