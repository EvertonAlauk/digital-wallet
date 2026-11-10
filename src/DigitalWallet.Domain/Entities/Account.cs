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
    /// <summary>Longest name an account can have, in characters.</summary>
    public const int MaxNameLength = 60;

    private Account(Guid id, Guid ownerId, DateTimeOffset createdAt, AccountKind kind, string? name)
    {
        Id = id;
        OwnerId = ownerId;
        CreatedAt = createdAt;
        Kind = kind;
        Name = name;
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

    /// <summary>Name chosen by the owner, or null when none was given.</summary>
    public string? Name { get; private set; }

    public AccountKind Kind { get; }

    public Money Balance { get; private set; }

    public AccountStatus Status { get; private set; }

    /// <summary>Optimistic concurrency token. Incremented on every movement and status change.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Opens a customer account. The name is optional: blank names are stored as no name. Throws
    /// <see cref="InvalidAccountNameException"/> when the name is longer than <see cref="MaxNameLength"/> or has control characters.
    /// </summary>
    public static Account Open(Guid id, Guid ownerId, DateTimeOffset createdAt, string? name = null) =>
        new(id, ownerId, createdAt, AccountKind.Customer, NormalizeName(name));

    /// <summary>Trims the name. Returns null for blank input, and throws when the name breaks the naming rules.</summary>
    public static string? NormalizeName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidAccountNameException($"The account name can have at most {MaxNameLength} characters.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new InvalidAccountNameException("The account name cannot contain control characters.");
        }

        return trimmed;
    }

    /// <summary>Opens the settlement account that is the counterpart of deposits and withdrawals.</summary>
    public static Account OpenSettlement(Guid id, DateTimeOffset createdAt) =>
        new(id, Guid.Empty, createdAt, AccountKind.Settlement, name: null);

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
        AccountKind kind = AccountKind.Customer,
        string? name = null)
    {
        var account = new Account(id, ownerId, createdAt, kind, name)
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

    /// <summary>
    /// Changes the account name (blank clears it). Renaming changes the stored row, so it increments <see cref="Version"/>.
    /// The name is checked with <see cref="NormalizeName"/>.
    /// </summary>
    public void Rename(string? name)
    {
        var normalized = NormalizeName(name);
        if (normalized == Name)
        {
            return;
        }

        Name = normalized;
        Version++;
    }

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
    /// Checks whether a ledger entry can be applied, without changing anything. Throws the business rule violation
    /// that applying it would cause. Use it to validate a whole transaction before any balance is touched.
    /// </summary>
    public void EnsureCanApply(LedgerEntry entry)
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
    }

    /// <summary>Applies a ledger entry to the balance, after checking it with <see cref="EnsureCanApply"/>.</summary>
    public void ApplyEntry(LedgerEntry entry)
    {
        EnsureCanApply(entry);

        Balance = Balance.Add(entry.Amount);
        Version++;
    }
}
