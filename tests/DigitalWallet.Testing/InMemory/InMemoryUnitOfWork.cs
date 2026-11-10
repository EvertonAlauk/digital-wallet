using DigitalWallet.Application.Common;
using DigitalWallet.Application.Operations;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Testing.InMemory;

/// <summary>
/// Unit of work over <see cref="InMemoryDatabase"/>. Keeps one working copy per account (identity map),
/// remembers the version each account had when it was first read, and publishes everything atomically on commit,
/// including outbox messages and operations. Row locks taken by <see cref="IAccountRepository.GetByIdsForUpdateAsync"/>
/// are held until commit or dispose. Not thread-safe by itself: each operation should use its own unit of work.
/// </summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork, IDisposable
{
    private readonly InMemoryDatabase _database;
    private readonly Dictionary<Guid, Account> _accounts = new();
    private readonly Dictionary<Guid, int> _readVersions = new();
    private readonly List<Account> _newAccounts = new();
    private readonly List<Transaction> _newTransactions = new();
    private readonly List<OutboxMessage> _newOutbox = new();
    private readonly List<Operation> _operationWrites = new();
    private readonly List<SemaphoreSlim> _heldLocks = new();
    private bool _disposed;

    internal InMemoryUnitOfWork(InMemoryDatabase database)
    {
        _database = database;
        Accounts = new AccountRepository(this);
        Transactions = new TransactionRepository(this);
        Operations = new OperationRepository(this);
        Outbox = new OutboxStaging(this);
    }

    public IAccountRepository Accounts { get; }

    public ITransactionRepository Transactions { get; }

    public IOperationRepository Operations { get; }

    /// <summary>Outbox of this unit of work. Messages are published to the committed store only on commit.</summary>
    public IOutbox Outbox { get; }

    // The fake has no database transaction to open: the publish step in CommitAsync is already all-or-nothing.
    public Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var changedAccounts = _accounts.Values
            .Where(account => _readVersions.ContainsKey(account.Id))
            .Select(account => (account, _readVersions[account.Id]))
            .ToList();

        try
        {
            _database.Commit(changedAccounts, _newAccounts, _newTransactions, _newOutbox, _operationWrites);
        }
        finally
        {
            // The transaction is over, successful or not, so its row locks are released.
            ReleaseLocks();
        }

        // Success: the committed versions become the baseline for the next commit of this unit of work.
        foreach (var account in _accounts.Values)
        {
            _readVersions[account.Id] = account.Version;
        }

        _newAccounts.Clear();
        _newTransactions.Clear();
        _newOutbox.Clear();
        _operationWrites.Clear();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseLocks();
    }

    private void ReleaseLocks()
    {
        foreach (var gate in _heldLocks)
        {
            gate.Release();
        }

        _heldLocks.Clear();
    }

    // Ascending id order is the lock order for every operation. Two transfers A→B and B→A therefore take
    // A before B and cannot wait on each other in a cycle.
    internal async Task<IReadOnlyDictionary<Guid, Account>> LockAccountsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var ordered = ids.Distinct().OrderBy(id => id).ToList();

        foreach (var id in ordered)
        {
            var gate = _database.GetRowLock(id);
            await gate.WaitAsync(cancellationToken);

            // Disposed while this request was waiting: give the lock back instead of keeping it forever.
            if (_disposed)
            {
                gate.Release();
                ReleaseLocks();
                throw new ObjectDisposedException(nameof(InMemoryUnitOfWork));
            }

            _heldLocks.Add(gate);
        }

        var locked = new Dictionary<Guid, Account>();
        foreach (var id in ordered)
        {
            var account = await FindAccountAsync(id, cancellationToken)
                ?? throw new AccountNotFoundException(id);
            locked[id] = account;
        }

        return locked;
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

    private Task<Operation?> FindOperationAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The latest staged write wins, so a unit of work sees its own changes.
        var staged = _operationWrites.LastOrDefault(operation => operation.Id == id);
        return Task.FromResult<Operation?>(staged ?? _database.GetOperation(id));
    }

    private Task AddOperationAsync(Operation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operationWrites.Add(operation);
        return Task.CompletedTask;
    }

    private Task UpdateOperationAsync(Operation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operationWrites.Add(operation);
        return Task.CompletedTask;
    }

    private sealed class AccountRepository(InMemoryUnitOfWork unitOfWork) : IAccountRepository
    {
        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            unitOfWork.FindAccountAsync(id, cancellationToken);

        public Task AddAsync(Account account, CancellationToken cancellationToken) =>
            unitOfWork.AddAccountAsync(account, cancellationToken);

        public Task<IReadOnlyList<Account>> ListByOwnerAsync(
            Guid ownerId,
            AccountStatus? status,
            int? limit,
            int offset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(unitOfWork._database.ListAccountsByOwner(ownerId, status, limit, offset));
        }

        public Task<int> CountByOwnerAsync(Guid ownerId, AccountStatus? status, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(unitOfWork._database.CountAccountsByOwner(ownerId, status));
        }

        public Task<IReadOnlyDictionary<Guid, Account>> GetByIdsForUpdateAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken) =>
            unitOfWork.LockAccountsAsync(ids, cancellationToken);
    }

    private sealed class TransactionRepository(InMemoryUnitOfWork unitOfWork) : ITransactionRepository
    {
        public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            unitOfWork.FindTransactionAsync(id, cancellationToken);

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken) =>
            unitOfWork.AddTransactionAsync(transaction, cancellationToken);
    }

    private sealed class OperationRepository(InMemoryUnitOfWork unitOfWork) : IOperationRepository
    {
        public Task AddAsync(Operation operation, CancellationToken cancellationToken) =>
            unitOfWork.AddOperationAsync(operation, cancellationToken);

        public Task<Operation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            unitOfWork.FindOperationAsync(id, cancellationToken);

        public Task UpdateAsync(Operation operation, CancellationToken cancellationToken) =>
            unitOfWork.UpdateOperationAsync(operation, cancellationToken);
    }

    private sealed class OutboxStaging(InMemoryUnitOfWork unitOfWork) : IOutbox
    {
        public void Enqueue(OutboxMessage message) => unitOfWork._newOutbox.Add(message);
    }
}
