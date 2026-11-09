using DigitalWallet.Application.Common;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.ValueObjects;
using DigitalWallet.Testing.InMemory;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Fakes;

// Validates the row-lock stand-in that transfers rely on.
public class RowLockTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedAsync(InMemoryDatabase database)
    {
        using var unitOfWork = database.CreateUnitOfWork();
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(10m), Guid.NewGuid()));
        await unitOfWork.Accounts.AddAsync(account, CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);
        return account.Id;
    }

    [Fact]
    public async Task LockRequestsInOppositeOrder_BothComplete()
    {
        var database = new InMemoryDatabase();
        var a = await SeedAsync(database);
        var b = await SeedAsync(database);

        // Each unit of work holds its locks briefly, then releases them, as a real transfer does at commit.
        async Task<int> LockThenRelease(Guid[] order)
        {
            using var unitOfWork = database.CreateUnitOfWork();
            var locked = await unitOfWork.Accounts.GetByIdsForUpdateAsync(order, CancellationToken.None);
            await Task.Delay(20);
            return locked.Count;
        }

        var results = await Task.WhenAll(LockThenRelease([a, b]), LockThenRelease([b, a])).WaitAsync(Guard);

        results.Should().AllBeEquivalentTo(2);
    }

    [Fact]
    public async Task SecondLockRequest_WaitsUntilTheHolderFinishes()
    {
        var database = new InMemoryDatabase();
        var account = await SeedAsync(database);

        using var holder = database.CreateUnitOfWork();
        await holder.Accounts.GetByIdsForUpdateAsync([account], CancellationToken.None);

        using var waiter = database.CreateUnitOfWork();
        var waiting = waiter.Accounts.GetByIdsForUpdateAsync([account], CancellationToken.None);

        var finishedEarly = await Task.WhenAny(waiting, Task.Delay(200)) == waiting;
        finishedEarly.Should().BeFalse("the lock is still held by the first unit of work");

        holder.Dispose();
        (await waiting.WaitAsync(Guard)).Should().ContainKey(account);
    }

    [Fact]
    public async Task LockReleasedOnCommit_IsAvailableToTheNextUnitOfWork()
    {
        var database = new InMemoryDatabase();
        var account = await SeedAsync(database);

        using (var holder = database.CreateUnitOfWork())
        {
            await holder.Accounts.GetByIdsForUpdateAsync([account], CancellationToken.None);
            await holder.CommitAsync(CancellationToken.None);
        }

        using var next = database.CreateUnitOfWork();
        var locked = next.Accounts.GetByIdsForUpdateAsync([account], CancellationToken.None).WaitAsync(Guard);
        (await locked).Should().ContainKey(account);
    }

    [Fact]
    public async Task MissingAccountInLockRequest_ThrowsAndReleasesEarlierLocks()
    {
        var database = new InMemoryDatabase();
        var existing = await SeedAsync(database);
        var missing = Guid.NewGuid();

        using (var failing = database.CreateUnitOfWork())
        {
            var act = () => failing.Accounts.GetByIdsForUpdateAsync([existing, missing], CancellationToken.None);
            await act.Should().ThrowAsync<AccountNotFoundException>();
        }

        using var next = database.CreateUnitOfWork();
        (await next.Accounts.GetByIdsForUpdateAsync([existing], CancellationToken.None).WaitAsync(Guard))
            .Should().ContainKey(existing);
    }
}
