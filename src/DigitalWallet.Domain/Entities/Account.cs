using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Domain.Entities;

/// <summary>
/// Account of a holder. The balance is a cache of the ledger entries and changes only via <see cref="ApplyEntry"/>.
/// Customer accounts never go negative. Settlement accounts represent money held outside the wallet
/// (the counterpart of deposits and withdrawals), so their balance is allowed to be negative.
/// </summary>
public sealed class Account
{
    private Account(Guid id, Guid ownerId, DateTimeOffset createdAt, AccountKind kind)
    {
        Id = id;
        OwnerId = ownerId;
        CreatedAt = createdAt;
        Kind = kind;
        Balance = Money.Zero();
        Status = AccountStatus.Active;
        Version = 0;
    }

    // Used by EF Core to materialize entities. Values are set by the persistence mapping.
    private Account()
    {
        Balance = Money.Zero();
    }

    public Guid Id { get; }

    public Guid OwnerId { get; }

    public AccountKind Kind { get; }

    public Money Balance { get; private set; }

    public AccountStatus Status { get; private set; }

    /// <summary>Optimistic concurrency token. Incremented on every movement and status change.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static Account Open(Guid id, Guid ownerId, DateTimeOffset createdAt) =>
        new(id, ownerId, createdAt, AccountKind.Customer);

    /// <summary>Opens the settlement account that is the counterpart of deposits and withdrawals.</summary>
    public static Account OpenSettlement(Guid id, DateTimeOffset createdAt) =>
        new(id, Guid.Empty, createdAt, AccountKind.Settlement);

    /// <summary>
    /// Restores an account from persisted state. Does not validate business rules, because the
    /// state was already validated when it was written.
    /// </summary>
    public static Account Rehydrate(
        Guid id,
        Guid ownerId,
        Money balance,
        AccountStatus status,
        int version,
        DateTimeOffset createdAt,
        AccountKind kind = AccountKind.Customer)
    {
        var account = new Account(id, ownerId, createdAt, kind)
        {
            Balance = balance,
            Status = status,
            Version = version,
        };

        return account;
    }

    /// <summary>Blocks the account. Changing the status is a state change, so it increments <see cref="Version"/>.</summary>
    public void Block() => ChangeStatus(AccountStatus.Blocked);

    public void Unblock() => ChangeStatus(AccountStatus.Active);

    private void ChangeStatus(AccountStatus newStatus)
    {
        if (Status == newStatus)
        {
            return;
        }

        Status = newStatus;
        Version++;
    }

    /// <summary>
    /// Applies a ledger entry to the balance. A blocked account cannot move funds. A customer account
    /// cannot go negative; a settlement account can.
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

        if (Kind == AccountKind.Customer && newBalance.Amount < 0m)
        {
            throw new InsufficientFundsException(Id, Balance.Amount, -entry.Amount.Amount);
        }

        Balance = newBalance;
        Version++;
    }
}
