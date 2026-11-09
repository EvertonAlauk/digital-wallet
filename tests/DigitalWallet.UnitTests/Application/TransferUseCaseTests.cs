using DigitalWallet.Application.Common;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class TransferUseCaseTests
{
    // Guards every concurrent test: a deadlock fails the test instead of hanging the run.
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Transfer_MovesMoneyWithTwoBalancedEntries()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        var result = await harness.TransferAsync(source, destination, 40m);

        result.SourceBalance.Should().Be(60m);
        result.DestinationBalance.Should().Be(40m);
        harness.Database.GetAccount(source)!.Balance.Should().Be(Money.Brl(60m));
        harness.Database.GetAccount(destination)!.Balance.Should().Be(Money.Brl(40m));

        var transaction = harness.Database.GetTransaction(result.TransactionId);
        transaction!.Type.Should().Be(TransactionType.Transfer);
        transaction.Entries.Should().HaveCount(2);
        transaction.Entries.Sum(entry => entry.Amount.Amount).Should().Be(0m);
    }

    [Fact]
    public async Task Transfer_ToSameAccount_IsRejectedAndChangesNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var account = await harness.CreateFundedAccountAsync(100m);

        Func<Task> act = () => harness.TransferAsync(account, account, 10m);

        await act.Should().ThrowAsync<InvalidTransactionException>();
        harness.Database.GetAccount(account)!.Balance.Should().Be(Money.Brl(100m));
        harness.Database.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_ChangesNothingAndReleasesLocks()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(50m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        Func<Task> rejected = () => harness.TransferAsync(source, destination, 60m);

        await rejected.Should().ThrowAsync<InsufficientFundsException>();
        harness.Database.GetAccount(source)!.Balance.Should().Be(Money.Brl(50m));
        harness.Database.GetAccount(destination)!.Balance.Should().Be(Money.Brl(0m));

        // The rejected transfer held the locks. They must be released, or this call would wait forever.
        var retry = harness.TransferAsync(source, destination, 50m).WaitAsync(DeadlockGuard);
        (await retry).SourceBalance.Should().Be(0m);
    }

    [Fact]
    public async Task Transfer_UnknownDestination_ThrowsNotFoundAndChangesNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var unknown = Guid.NewGuid();

        Func<Task> act = () => harness.TransferAsync(source, unknown, 10m);

        await act.Should().ThrowAsync<AccountNotFoundException>();
        harness.Database.GetAccount(source)!.Balance.Should().Be(Money.Brl(100m));
    }

    [Fact]
    public async Task Transfer_BlockedDestination_ThrowsBusinessRuleAndChangesNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);
        await harness.BlockAccountAsync(destination);

        Func<Task> act = () => harness.TransferAsync(source, destination, 10m);

        await act.Should().ThrowAsync<AccountBlockedException>();
        harness.Database.GetAccount(source)!.Balance.Should().Be(Money.Brl(100m));
    }

    [Fact]
    public async Task Transfer_NonPositiveAmount_IsRejected()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        Func<Task> act = () => harness.TransferAsync(source, destination, 0m);

        await act.Should().ThrowAsync<InvalidAmountException>();
    }

    [Fact]
    public async Task Transfer_TooManyDecimals_ThrowsValidation()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        Func<Task> act = () => harness.TransferAsync(source, destination, 1.001m);

        await act.Should().ThrowAsync<RequestValidationException>();
    }

    [Fact]
    public async Task ConcurrentTransfers_FromOneAccount_NeverOverdraw()
    {
        // Ten transfers of 30 race for a balance of 100. Exactly three can succeed, and the balance is never negative.
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        var attempts = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            try
            {
                await harness.TransferAsync(source, destination, 30m);
                return true;
            }
            catch (InsufficientFundsException)
            {
                return false;
            }
        }));

        var outcomes = await Task.WhenAll(attempts).WaitAsync(DeadlockGuard);

        outcomes.Count(succeeded => succeeded).Should().Be(3);
        harness.Database.GetAccount(source)!.Balance.Amount.Should().Be(10m);
        harness.Database.GetAccount(destination)!.Balance.Amount.Should().Be(90m);
    }

    [Fact]
    public async Task OppositeDirections_RunningConcurrently_CompleteWithoutDeadlock()
    {
        // A→B and B→A at the same time. Ascending-id lock order means neither waits on the other in a cycle.
        var harness = await WalletHarness.CreateAsync();
        var a = await harness.CreateFundedAccountAsync(1000m);
        var b = await harness.CreateFundedAccountAsync(1000m);

        var transfers = Enumerable.Range(0, 100).Select(index => Task.Run(() =>
            index % 2 == 0
                ? harness.TransferAsync(a, b, 1m)
                : harness.TransferAsync(b, a, 2m)));

        await Task.WhenAll(transfers).WaitAsync(DeadlockGuard);

        // 50 transfers of 1 from A to B, and 50 transfers of 2 from B to A.
        harness.Database.GetAccount(a)!.Balance.Amount.Should().Be(1050m);
        harness.Database.GetAccount(b)!.Balance.Amount.Should().Be(950m);
    }
}
