using DigitalWallet.Application.Exceptions;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.ValueObjects;
using DigitalWallet.Testing.InMemory;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Fakes;

// These tests validate the in-memory fakes that stand in for the database in other tests.
public class InMemoryDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedAccountAsync(InMemoryDatabase database, decimal balance)
    {
        var unitOfWork = database.CreateUnitOfWork();
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);

        if (balance != 0m)
        {
            account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(balance), Guid.NewGuid()));
        }

        await unitOfWork.Accounts.AddAsync(account, CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);
        return account.Id;
    }

    private static async Task CreditAsync(InMemoryUnitOfWork unitOfWork, Guid accountId, decimal amount)
    {
        var account = await unitOfWork.Accounts.GetByIdAsync(accountId, CancellationToken.None)
            ?? throw new InvalidOperationException("Account not found.");
        account.ApplyEntry(new LedgerEntry(accountId, Money.Brl(amount), Guid.NewGuid()));
    }

    [Fact]
    public async Task Commit_PersistsChangesVisibleToLaterReaders()
    {
        var database = new InMemoryDatabase();
        var accountId = await SeedAccountAsync(database, 100m);
        var unitOfWork = database.CreateUnitOfWork();

        var account = await unitOfWork.Accounts.GetByIdAsync(accountId, CancellationToken.None);
        account!.ApplyEntry(new LedgerEntry(accountId, Money.Brl(-30m), Guid.NewGuid()));
        await unitOfWork.CommitAsync(CancellationToken.None);

        var stored = database.GetAccount(accountId);
        stored!.Balance.Should().Be(Money.Brl(70m));
        stored.Version.Should().Be(2);
    }

    [Fact]
    public async Task UncommittedChanges_AreNotVisibleOutsideTheUnitOfWork()
    {
        var database = new InMemoryDatabase();
        var accountId = await SeedAccountAsync(database, 100m);
        var unitOfWork = database.CreateUnitOfWork();

        await CreditAsync(unitOfWork, accountId, 10m);

        database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(100m));
    }

    [Fact]
    public async Task Commit_WithoutConflict_PersistsAccountsAndTransaction()
    {
        var database = new InMemoryDatabase();
        var fromId = await SeedAccountAsync(database, 100m);
        var toId = await SeedAccountAsync(database, 0m);
        var unitOfWork = database.CreateUnitOfWork();
        var transfer = Transaction.Transfer(Guid.NewGuid(), fromId, toId, Money.Brl(40m), Now);

        var from = await unitOfWork.Accounts.GetByIdAsync(fromId, CancellationToken.None);
        var to = await unitOfWork.Accounts.GetByIdAsync(toId, CancellationToken.None);
        foreach (var entry in transfer.Entries)
        {
            (entry.AccountId == fromId ? from! : to!).ApplyEntry(entry);
        }

        await unitOfWork.Transactions.AddAsync(transfer, CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);

        database.GetAccount(fromId)!.Balance.Should().Be(Money.Brl(60m));
        database.GetAccount(toId)!.Balance.Should().Be(Money.Brl(40m));
        database.TransactionCount.Should().Be(1);
        database.GetTransaction(transfer.Id)!.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Commit_WhenAnyAccountIsStale_AppliesNothing()
    {
        // Atomicity: the first unit of work changes two accounts and adds a transaction,
        // but the second account was changed by someone else. Nothing from the first unit may be written.
        var database = new InMemoryDatabase();
        var fromId = await SeedAccountAsync(database, 100m);
        var toId = await SeedAccountAsync(database, 0m);
        var transfer = Transaction.Transfer(Guid.NewGuid(), fromId, toId, Money.Brl(40m), Now);

        var first = database.CreateUnitOfWork();
        var from = await first.Accounts.GetByIdAsync(fromId, CancellationToken.None);
        var to = await first.Accounts.GetByIdAsync(toId, CancellationToken.None);
        foreach (var entry in transfer.Entries)
        {
            (entry.AccountId == fromId ? from! : to!).ApplyEntry(entry);
        }

        await first.Transactions.AddAsync(transfer, CancellationToken.None);

        var second = database.CreateUnitOfWork();
        await CreditAsync(second, toId, 5m);
        await second.CommitAsync(CancellationToken.None);

        Func<Task> act = () => first.CommitAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        database.GetAccount(fromId)!.Balance.Should().Be(Money.Brl(100m));
        database.GetAccount(toId)!.Balance.Should().Be(Money.Brl(5m));
        database.TransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task Commit_WhenAccountChangedSinceRead_ThrowsConcurrencyConflict()
    {
        var database = new InMemoryDatabase();
        var accountId = await SeedAccountAsync(database, 100m);
        var first = database.CreateUnitOfWork();
        await CreditAsync(first, accountId, 10m);

        var second = database.CreateUnitOfWork();
        await CreditAsync(second, accountId, 20m);
        await second.CommitAsync(CancellationToken.None);

        Func<Task> act = () => first.CommitAsync(CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        exception.Which.EntityId.Should().Be(accountId);
        database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(120m));
    }

    [Fact]
    public async Task Commit_AfterSuccess_CanCommitAgainWithoutConflict()
    {
        var database = new InMemoryDatabase();
        var accountId = await SeedAccountAsync(database, 0m);
        var unitOfWork = database.CreateUnitOfWork();

        await CreditAsync(unitOfWork, accountId, 10m);
        await unitOfWork.CommitAsync(CancellationToken.None);
        await CreditAsync(unitOfWork, accountId, 5m);
        await unitOfWork.CommitAsync(CancellationToken.None);

        database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(15m));
    }

    [Fact]
    public async Task BlockByAnotherUnitOfWork_ConflictsWithStaleBalanceChange()
    {
        // Regression: blocking an account must bump the version, otherwise a stale balance change
        // could overwrite the block without a conflict being detected.
        var database = new InMemoryDatabase();
        var accountId = await SeedAccountAsync(database, 100m);
        var stale = database.CreateUnitOfWork();
        await CreditAsync(stale, accountId, 10m);

        var blocker = database.CreateUnitOfWork();
        (await blocker.Accounts.GetByIdAsync(accountId, CancellationToken.None))!.Block();
        await blocker.CommitAsync(CancellationToken.None);

        Func<Task> act = () => stale.CommitAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        database.GetAccount(accountId)!.Status.Should().Be(AccountStatus.Blocked);
    }

    [Fact]
    public async Task ConcurrentCredits_WithRetryOnConflict_AreAllApplied()
    {
        var database = new InMemoryDatabase();
        var accountId = await SeedAccountAsync(database, 0m);
        const int workers = 20;
        const int creditsPerWorker = 5;

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < creditsPerWorker; i++)
            {
                await CreditWithRetryAsync(database, accountId, 1m);
            }
        }));

        await Task.WhenAll(tasks);

        var stored = database.GetAccount(accountId);
        stored!.Balance.Should().Be(Money.Brl(workers * creditsPerWorker));
        stored.Version.Should().Be(workers * creditsPerWorker);
    }

    [Fact]
    public async Task Commit_DuplicateTransactionId_Throws()
    {
        var database = new InMemoryDatabase();
        var accountId = await SeedAccountAsync(database, 100m);
        var otherId = await SeedAccountAsync(database, 0m);
        var transfer = Transaction.Transfer(Guid.NewGuid(), accountId, otherId, Money.Brl(1m), Now);

        var first = database.CreateUnitOfWork();
        await first.Transactions.AddAsync(transfer, CancellationToken.None);
        await first.CommitAsync(CancellationToken.None);

        var second = database.CreateUnitOfWork();
        await second.Transactions.AddAsync(transfer, CancellationToken.None);

        Func<Task> act = () => second.CommitAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        database.TransactionCount.Should().Be(1);
    }

    private static async Task CreditWithRetryAsync(InMemoryDatabase database, Guid accountId, decimal amount)
    {
        while (true)
        {
            var unitOfWork = database.CreateUnitOfWork();
            await CreditAsync(unitOfWork, accountId, amount);

            try
            {
                await unitOfWork.CommitAsync(CancellationToken.None);
                return;
            }
            catch (ConcurrencyConflictException)
            {
                // Another worker committed first. Reload and try again.
            }
        }
    }
}
