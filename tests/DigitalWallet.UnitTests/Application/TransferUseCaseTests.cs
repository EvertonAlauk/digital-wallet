using DigitalWallet.Application.Common;
using DigitalWallet.Application.Operations;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Events;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class TransferUseCaseTests
{
    // Guards every concurrent test: a deadlock fails the test instead of hanging the run.
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Transfer_CompletesWithBothBalancesAndPublishesTransferCompleted()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        var operation = await harness.TransferAsync(source, destination, 40m);

        operation.Status.Should().Be(OperationStatus.Completed.ToString());
        harness.Database.GetAccount(source)!.Balance.Should().Be(Money.Brl(60m));
        harness.Database.GetAccount(destination)!.Balance.Should().Be(Money.Brl(40m));

        var transaction = harness.Database.GetTransaction(operation.TransactionId!.Value);
        transaction!.Type.Should().Be(TransactionType.Transfer);
        transaction.Entries.Should().HaveCount(2);
        transaction.Entries.Sum(entry => entry.Amount.Amount).Should().Be(0m);

        harness.Database.OutboxMessages.Should().Contain(m =>
            m.Message.Type == nameof(TransferCompleted) && m.PublishedAt != null);
    }

    [Fact]
    public async Task Transfer_ToSameAccount_IsRefusedAtAcceptance()
    {
        var harness = await WalletHarness.CreateAsync();
        var account = await harness.CreateFundedAccountAsync(100m);

        var outboxBefore = harness.Database.OutboxMessages.Count;
        Func<Task> act = () => harness.AcceptTransferOnlyAsync(account, account, 10m);

        await act.Should().ThrowAsync<InvalidTransactionException>();
        harness.Database.OutboxMessages.Count.Should().Be(outboxBefore, "a refused transfer leaves no event");
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_IsRejectedAndPublishesNoCompletion()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(50m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        var operation = await harness.TransferAsync(source, destination, 60m);

        operation.Status.Should().Be(OperationStatus.Rejected.ToString());
        operation.FailureReason.Should().Be("Insufficient funds.");
        harness.Database.GetAccount(source)!.Balance.Should().Be(Money.Brl(50m));
        harness.Database.GetAccount(destination)!.Balance.Should().Be(Money.Brl(0m));
        harness.Database.OutboxMessages.Should().NotContain(m => m.Message.Type == nameof(TransferCompleted));
    }

    [Fact]
    public async Task Transfer_UnknownDestination_IsRefusedAtAcceptance()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var unknown = Guid.NewGuid();

        Func<Task> act = () => harness.AcceptTransferOnlyAsync(source, unknown, 10m);

        var exception = await act.Should().ThrowAsync<AccountNotFoundException>();
        exception.Which.AccountId.Should().Be(unknown);
    }

    [Fact]
    public async Task Transfer_BlockedDestination_IsRejectedByTheRequestedStep()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);
        await harness.BlockAccountAsync(destination);

        var operation = await harness.TransferAsync(source, destination, 10m);

        operation.Status.Should().Be(OperationStatus.Rejected.ToString());
        operation.FailureReason.Should().Be("The destination account is blocked.");
        harness.Database.GetAccount(source)!.Balance.Should().Be(Money.Brl(100m));
    }

    [Fact]
    public async Task Transfer_TooManyDecimals_IsRefusedAtAcceptance()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        Func<Task> act = () => harness.AcceptTransferOnlyAsync(source, destination, 1.001m);

        await act.Should().ThrowAsync<RequestValidationException>();
    }

    [Fact]
    public async Task ConcurrentTransfers_FromOneAccount_NeverOverdraw()
    {
        // Ten transfers of 30 are accepted at the same time and race for a balance of 100.
        // Exactly three can complete, and the balance is never negative.
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);

        var accepted = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => harness.AcceptTransferOnlyAsync(source, destination, 30m))))
            .WaitAsync(DeadlockGuard);

        await harness.Pipeline.DrainAsync();

        var outcomes = await Task.WhenAll(accepted.Select(a => harness.OperationAsync(a.OperationId)));
        outcomes.Count(o => o.Status == OperationStatus.Completed.ToString()).Should().Be(3);
        outcomes.Count(o => o.Status == OperationStatus.Rejected.ToString()).Should().Be(7);
        harness.Database.GetAccount(source)!.Balance.Amount.Should().Be(10m);
        harness.Database.GetAccount(destination)!.Balance.Amount.Should().Be(90m);
    }

    [Fact]
    public async Task OppositeDirections_AcceptedConcurrently_AllComplete()
    {
        // A→B and B→A at the same time. Ascending-id lock order means neither waits on the other in a cycle.
        var harness = await WalletHarness.CreateAsync();
        var a = await harness.CreateFundedAccountAsync(1000m);
        var b = await harness.CreateFundedAccountAsync(1000m);

        var accepted = await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() =>
            index % 2 == 0
                ? harness.AcceptTransferOnlyAsync(a, b, 1m)
                : harness.AcceptTransferOnlyAsync(b, a, 2m))))
            .WaitAsync(DeadlockGuard);

        await harness.Pipeline.DrainAsync();

        var outcomes = await Task.WhenAll(accepted.Select(x => harness.OperationAsync(x.OperationId)));
        outcomes.Should().OnlyContain(o => o.Status == OperationStatus.Completed.ToString());

        // 50 transfers of 1 from A to B, and 50 transfers of 2 from B to A.
        harness.Database.GetAccount(a)!.Balance.Amount.Should().Be(1050m);
        harness.Database.GetAccount(b)!.Balance.Amount.Should().Be(950m);
    }
}
