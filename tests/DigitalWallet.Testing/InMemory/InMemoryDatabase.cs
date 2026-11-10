using System.Collections.Concurrent;
using DigitalWallet.Application.Exceptions;
using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Testing.InMemory;

/// <summary>
/// Thread-safe in-memory stand-in for the database. Each <see cref="InMemoryUnitOfWork"/> works on private
/// copies and publishes its changes in one step on commit. Before publishing, the commit checks the version of
/// every account it changed (compare-and-swap); if any check fails, nothing is written.
/// </summary>
public sealed class InMemoryDatabase
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _rowLocks = new();
    private readonly Dictionary<Guid, Account> _accounts = new();
    private readonly Dictionary<Guid, Transaction> _transactions = new();
    private readonly List<LedgerRow> _ledger = new();
    private long _lastSequence;
    private readonly List<OutboxRow> _outbox = new();
    private readonly Dictionary<Guid, Operation> _operations = new();

    /// <summary>Returns a copy of the committed operation, or null when it does not exist.</summary>
    public Operation? GetOperation(Guid id)
    {
        lock (_gate)
        {
            return _operations.TryGetValue(id, out var operation) ? CopyOperation(operation) : null;
        }
    }

    private static Operation CopyOperation(Operation operation) =>
        Operation.Rehydrate(
            operation.Id,
            operation.Kind,
            operation.AccountId,
            operation.DestinationAccountId,
            operation.Amount,
            operation.CreatedAt,
            operation.Status,
            operation.FailureReason,
            operation.TransactionId,
            operation.CompletedAt);

    private sealed record OutboxRow(long Sequence, OutboxMessage Message, DateTimeOffset? PublishedAt);

    public InMemoryUnitOfWork CreateUnitOfWork() => new(this);

    private readonly Dictionary<string, IdempotencyEntry> _idempotencyKeys = new();

    internal bool TryInsertIdempotencyEntry(IdempotencyEntry entry)
    {
        lock (_gate)
        {
            if (_idempotencyKeys.ContainsKey(entry.Key))
            {
                return false;
            }

            _idempotencyKeys[entry.Key] = entry;
            return true;
        }
    }

    internal IdempotencyEntry? FindIdempotencyEntry(string key)
    {
        lock (_gate)
        {
            return _idempotencyKeys.GetValueOrDefault(key);
        }
    }

    internal bool DeleteExpiredIdempotencyEntry(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_idempotencyKeys.TryGetValue(key, out var entry) && entry.ExpiresAt <= now)
            {
                _idempotencyKeys.Remove(key);
                return true;
            }

            return false;
        }
    }

    internal void CompleteIdempotencyEntry(string key, StoredResponse response)
    {
        lock (_gate)
        {
            if (_idempotencyKeys.TryGetValue(key, out var entry))
            {
                _idempotencyKeys[key] = entry with { Response = response };
            }
        }
    }

    internal void DeleteIdempotencyEntry(string key)
    {
        lock (_gate)
        {
            _idempotencyKeys.Remove(key);
        }
    }

    /// <summary>The lock that stands in for a row lock on one account. Held until the unit of work commits or is disposed.</summary>
    internal SemaphoreSlim GetRowLock(Guid accountId) =>
        _rowLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Returns a copy of the committed account, or null when it does not exist.</summary>
    public Account? GetAccount(Guid id)
    {
        lock (_gate)
        {
            return _accounts.TryGetValue(id, out var account) ? Copy(account) : null;
        }
    }

    /// <summary>Returns copies of the committed customer accounts of one owner, newest first.</summary>
    public int CountAccountsByOwner(Guid ownerId, AccountStatus? status)
    {
        lock (_gate)
        {
            return _accounts.Values.Count(account =>
                account.OwnerId == ownerId && account.Kind == AccountKind.Customer && (status is null || account.Status == status));
        }
    }

    public IReadOnlyList<Account> ListAccountsByOwner(Guid ownerId, AccountStatus? status, int? limit, int offset)
    {
        lock (_gate)
        {
            IEnumerable<Account> query = _accounts.Values
                .Where(account => account.OwnerId == ownerId && account.Kind == AccountKind.Customer)
                .Where(account => status is null || account.Status == status)
                .OrderByDescending(account => account.CreatedAt)
                .ThenBy(account => account.Id)
                .Skip(offset);

            if (limit is { } take)
            {
                query = query.Take(take);
            }

            return query.Select(Copy).ToList();
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

    /// <summary>Committed ledger entries of one account, most recent first, mirroring the statement query.</summary>
    public IReadOnlyList<LedgerRecord> ReadAccountEntries(Guid accountId, long? beforeSequence, int limit, LedgerFilter? filter = null)
    {
        var narrowing = filter ?? LedgerFilter.None;
        lock (_gate)
        {
            return _ledger
                .Where(row => row.AccountId == accountId)
                .Where(row => beforeSequence is null || row.Sequence < beforeSequence)
                .Where(row => narrowing.Type is null || row.Type == narrowing.Type)
                .Where(row => narrowing.From is null || row.CreatedAt >= narrowing.From)
                .Where(row => narrowing.To is null || row.CreatedAt < narrowing.To)
                .OrderByDescending(row => row.Sequence)
                .Take(limit)
                .Select(row => new LedgerRecord(row.Sequence, row.TransactionId, row.Type, row.Amount, row.CreatedAt))
                .ToList();
        }
    }

    /// <summary>Committed outbox messages, oldest first, with their publish time once published.</summary>
    public IReadOnlyList<(OutboxMessage Message, DateTimeOffset? PublishedAt)> OutboxMessages
    {
        get
        {
            lock (_gate)
            {
                return _outbox.Select(row => (row.Message, row.PublishedAt)).ToList();
            }
        }
    }

    internal IReadOnlyList<OutboxMessage> GetPendingOutbox(int limit)
    {
        lock (_gate)
        {
            return _outbox
                .Where(row => row.PublishedAt is null)
                .Take(limit)
                .Select(row => row.Message)
                .ToList();
        }
    }

    internal void MarkOutboxPublished(Guid messageId, DateTimeOffset publishedAt)
    {
        lock (_gate)
        {
            var index = _outbox.FindIndex(row => row.Message.Id == messageId);
            if (index >= 0)
            {
                _outbox[index] = _outbox[index] with { PublishedAt = publishedAt };
            }
        }
    }

    internal void Commit(
        IReadOnlyCollection<(Account Account, int ReadVersion)> changedAccounts,
        IReadOnlyCollection<Account> newAccounts,
        IReadOnlyCollection<Transaction> newTransactions,
        IReadOnlyCollection<OutboxMessage> newOutbox,
        IReadOnlyCollection<Operation> operationWrites)
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

            // Phase 2: publish. Nothing below can fail. Outbox messages commit together with the changes that produced them.
            foreach (var message in newOutbox)
            {
                _outbox.Add(new OutboxRow(++_lastSequence, message, PublishedAt: null));
            }

            foreach (var operation in operationWrites)
            {
                _operations[operation.Id] = CopyOperation(operation);
            }

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

                foreach (var entry in transaction.Entries)
                {
                    _ledger.Add(new LedgerRow(
                        ++_lastSequence,
                        entry.AccountId,
                        transaction.Id,
                        transaction.Type,
                        entry.Amount,
                        transaction.CreatedAt));
                }
            }
        }
    }

    private static Account Copy(Account account) =>
        Account.Rehydrate(
            account.Id,
            account.OwnerId,
            account.Balance,
            account.Status,
            account.Version,
            account.CreatedAt,
            account.Kind,
            account.Name);

    private static Transaction Copy(Transaction transaction) =>
        Transaction.Rehydrate(
            transaction.Id,
            transaction.Type,
            transaction.Status,
            transaction.CreatedAt,
            transaction.Entries);

    // Ledger row as stored; the public LedgerRecord does not expose the account, the query filters by it.
    private sealed record LedgerRow(
        long Sequence,
        Guid AccountId,
        Guid TransactionId,
        TransactionType Type,
        Money Amount,
        DateTimeOffset CreatedAt);
}
