using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Testing.InMemory;

/// <summary>
/// Unit of work over <see cref="InMemoryDatabase"/>. Keeps one working copy per account (identity map),
/// remembers the version each account had when it was first read, and publishes everything atomically on commit.
/// Not thread-safe by itself: each operation should use its own unit of work.
/// </summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly InMemoryDatabase _database;
    private readonly Dictionary<Guid, Account> _accounts = new();
    private readonly Dictionary<Guid, int> _readVersions = new();
    private readonly List<Account> _newAccounts = new();
    private readonly List<Transaction> _newTransactions = new();

    internal InMemoryUnitOfWork(InMemoryDatabase database)
    {
        _database = database;
        Accounts = new AccountRepository(this);
        Transactions = new TransactionRepository(this);
    }

    public IAccountRepository Accounts { get; }

    public ITransactionRepository Transactions { get; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var changedAccounts = _accounts.Values
            .Where(account => _readVersions.ContainsKey(account.Id))
            .Select(account => (account, _readVersions[account.Id]))
            .ToList();

        _database.Commit(changedAccounts, _newAccounts, _newTransactions);

        // Success: the committed versions become the baseline for the next commit of this unit of work.
        foreach (var account in _accounts.Values)
        {
            _readVersions[account.Id] = account.Version;
        }

        _newAccounts.Clear();
        _newTransactions.Clear();
        return Task.CompletedTask;
    }

    private Task<Account?> FindAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_accounts.TryGetValue(id, out var tracked))
        {
            return Task.FromResult<Account?>(tracked);
        }

        var committed = _database.GetAccount(id);
        if (committed is null)
        {
            return Task.FromResult<Account?>(null);
        }

        _accounts[id] = committed;
        _readVersions[id] = committed.Version;
        return Task.FromResult<Account?>(committed);
    }

    private Task AddAccountAsync(Account account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_accounts.ContainsKey(account.Id))
        {
            throw new InvalidOperationException($"Account {account.Id} is already tracked by this unit of work.");
        }

        _accounts[account.Id] = account;
        _newAccounts.Add(account);
        return Task.CompletedTask;
    }

    private Task<Transaction?> FindTransactionAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pending = _newTransactions.FirstOrDefault(transaction => transaction.Id == id);
        return Task.FromResult<Transaction?>(pending ?? _database.GetTransaction(id));
    }

    private Task AddTransactionAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_newTransactions.Any(pending => pending.Id == transaction.Id))
        {
            throw new InvalidOperationException($"Transaction {transaction.Id} was already added.");
        }

        _newTransactions.Add(transaction);
        return Task.CompletedTask;
    }

    private sealed class AccountRepository(InMemoryUnitOfWork unitOfWork) : IAccountRepository
    {
        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            unitOfWork.FindAccountAsync(id, cancellationToken);

        public Task AddAsync(Account account, CancellationToken cancellationToken) =>
            unitOfWork.AddAccountAsync(account, cancellationToken);
    }

    private sealed class TransactionRepository(InMemoryUnitOfWork unitOfWork) : ITransactionRepository
    {
        public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            unitOfWork.FindTransactionAsync(id, cancellationToken);

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken) =>
            unitOfWork.AddTransactionAsync(transaction, cancellationToken);
    }
}
