using DigitalWallet.Application.Exceptions;
using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Testing.InMemory;

/// <summary>
/// Thread-safe in-memory stand-in for the database. Each <see cref="InMemoryUnitOfWork"/> works on private
/// copies and publishes its changes in one step on commit. Before publishing, the commit checks the version of
/// every account it changed (compare-and-swap); if any check fails, nothing is written.
/// </summary>
public sealed class InMemoryDatabase
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Account> _accounts = new();
    private readonly Dictionary<Guid, Transaction> _transactions = new();

    public InMemoryUnitOfWork CreateUnitOfWork() => new(this);

    /// <summary>Returns a copy of the committed account, or null when it does not exist.</summary>
    public Account? GetAccount(Guid id)
    {
        lock (_gate)
        {
            return _accounts.TryGetValue(id, out var account) ? Copy(account) : null;
        }
    }

    /// <summary>Returns a copy of the committed transaction, or null when it does not exist.</summary>
    public Transaction? GetTransaction(Guid id)
    {
        lock (_gate)
        {
            return _transactions.TryGetValue(id, out var transaction) ? Copy(transaction) : null;
        }
    }

    public int TransactionCount
    {
        get
        {
            lock (_gate)
            {
                return _transactions.Count;
            }
        }
    }

    internal void Commit(
        IReadOnlyCollection<(Account Account, int ReadVersion)> changedAccounts,
        IReadOnlyCollection<Account> newAccounts,
        IReadOnlyCollection<Transaction> newTransactions)
    {
        lock (_gate)
        {
            // Phase 1: validate everything. Nothing is written if any check fails.
            foreach (var (account, readVersion) in changedAccounts)
            {
                if (!_accounts.TryGetValue(account.Id, out var committed) || committed.Version != readVersion)
                {
                    throw new ConcurrencyConflictException(nameof(Account), account.Id);
                }
            }

            foreach (var account in newAccounts)
            {
                if (_accounts.ContainsKey(account.Id))
                {
                    throw new InvalidOperationException($"Account {account.Id} already exists.");
                }
            }

            foreach (var transaction in newTransactions)
            {
                if (_transactions.ContainsKey(transaction.Id))
                {
                    throw new InvalidOperationException($"Transaction {transaction.Id} already exists.");
                }
            }

            // Phase 2: publish. Nothing below can fail.
            foreach (var (account, _) in changedAccounts)
            {
                _accounts[account.Id] = Copy(account);
            }

            foreach (var account in newAccounts)
            {
                _accounts[account.Id] = Copy(account);
            }

            foreach (var transaction in newTransactions)
            {
                _transactions[transaction.Id] = Copy(transaction);
            }
        }
    }

    private static Account Copy(Account account) =>
        Account.Rehydrate(account.Id, account.OwnerId, account.Balance, account.Status, account.Version, account.CreatedAt);

    private static Transaction Copy(Transaction transaction) =>
        Transaction.Rehydrate(
            transaction.Id,
            transaction.Type,
            transaction.Status,
            transaction.CreatedAt,
            transaction.Entries);
}
