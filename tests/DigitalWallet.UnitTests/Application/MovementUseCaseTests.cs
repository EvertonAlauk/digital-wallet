using DigitalWallet.Application.Common;
using DigitalWallet.Application.Operations;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class MovementUseCaseTests
{
    [Fact]
    public async Task Deposit_CompletesAndCreditsCustomerAndDebitsSettlement()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        var operation = await harness.DepositAsync(accountId, 100m);

        operation.Kind.Should().Be(OperationKind.Deposit.ToString());
        operation.Status.Should().Be(OperationStatus.Completed.ToString());
        operation.TransactionId.Should().NotBeNull();
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(100m));
        harness.Database.GetAccount(SettlementAccount.Id)!.Balance.Should().Be(Money.Brl(-100m));
    }

    [Fact]
    public async Task Deposit_Acceptance_ReturnsBeforeAnyBalanceMoves()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        var accepted = await harness.AcceptDepositOnlyAsync(accountId, 40m);

        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(0m));
        (await harness.OperationAsync(accepted.OperationId)).Status.Should().Be(OperationStatus.Accepted.ToString());
    }

    [Fact]
    public async Task Deposit_UnknownAccount_IsRefusedAtAcceptance()
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.AcceptDepositOnlyAsync(Guid.NewGuid(), 10m);

        await act.Should().ThrowAsync<AccountNotFoundException>();
        harness.Database.OutboxMessages.Should().BeEmpty("nothing is accepted, so nothing is published");
    }

    [Fact]
    public async Task Deposit_TooManyDecimals_IsRefusedAtAcceptance()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        Func<Task> act = () => harness.AcceptDepositOnlyAsync(accountId, 10.001m);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("amount");
    }

    [Fact]
    public async Task Deposit_ZeroAmount_IsRefusedAtAcceptance()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        Func<Task> act = () => harness.AcceptDepositOnlyAsync(accountId, 0m);

        await act.Should().ThrowAsync<InvalidAmountException>();
    }

    [Fact]
    public async Task Deposit_BlockedAccount_IsRejectedByTheRequestedStep()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(10m);
        await harness.BlockAccountAsync(accountId);

        var operation = await harness.DepositAsync(accountId, 5m);

        operation.Status.Should().Be(OperationStatus.Rejected.ToString());
        operation.FailureReason.Should().Be("The account is blocked.");
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(10m));
        harness.Notifications.Sent.Should().ContainSingle(n => n.Text.Contains("was rejected"));
    }

    [Fact]
    public async Task Withdrawal_CompletesAndMovesMoneyBackToSettlement()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(100m);

        var operation = await harness.WithdrawAsync(accountId, 30m);

        operation.Status.Should().Be(OperationStatus.Completed.ToString());
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(70m));
        harness.Database.GetAccount(SettlementAccount.Id)!.Balance.Should().Be(Money.Brl(-70m));
    }

    [Fact]
    public async Task Withdrawal_InsufficientFunds_IsRejectedAndChangesNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(50m);

        var operation = await harness.WithdrawAsync(accountId, 60m);

        operation.Status.Should().Be(OperationStatus.Rejected.ToString());
        operation.FailureReason.Should().Be("Insufficient funds.");
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(50m));
    }

    [Fact]
    public async Task Withdrawals_RacingForOneBalance_OnlyOneCompletes()
    {
        // Both pass the advisory check on the requested step, because the balance is still 100 for each.
        // The created step validates under lock, so the second withdrawal is rejected and the balance never goes negative.
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(100m);

        var first = await harness.AcceptWithdrawalOnlyAsync(accountId, 60m);
        var second = await harness.AcceptWithdrawalOnlyAsync(accountId, 60m);
        await harness.Pipeline.DrainAsync();

        var outcomes = new[]
        {
            await harness.OperationAsync(first.OperationId),
            await harness.OperationAsync(second.OperationId),
        };

        outcomes.Should().ContainSingle(o => o.Status == OperationStatus.Completed.ToString());
        outcomes.Should().ContainSingle(o => o.Status == OperationStatus.Rejected.ToString() && o.FailureReason!.StartsWith("Insufficient funds"));
        harness.Database.GetAccount(accountId)!.Balance.Amount.Should().Be(40m);
    }

    [Fact]
    public async Task DuplicateCreatedMessage_DoesNotMoveMoneyTwice()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);
        await harness.DepositAsync(accountId, 25m);

        var created = harness.Database.OutboxMessages.Single(m => m.Message.Type == nameof(DepositCreated)).Message;
        await harness.Pipeline.DispatchAsync(created, CancellationToken.None);

        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(25m));
    }

    [Fact]
    public async Task ConcurrentDuplicateCreatedMessages_MoveMoneyOnce()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);
        var accepted = await harness.AcceptDepositOnlyAsync(accountId, 15m);

        var requested = harness.Database.OutboxMessages.Single(m => m.Message.Type == nameof(DepositRequested)).Message;
        await harness.Pipeline.DispatchAsync(requested, CancellationToken.None);
        var created = harness.Database.OutboxMessages.Single(m => m.Message.Type == nameof(DepositCreated)).Message;

        // Two deliveries of the same Created message at the same time: the lock makes the second one wait,
        // and then it sees that the operation is already completed.
        await Task.WhenAll(
            harness.Pipeline.DispatchAsync(created, CancellationToken.None),
            harness.Pipeline.DispatchAsync(created, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(30));

        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(15m));
        (await harness.OperationAsync(accepted.OperationId)).Status.Should().Be(OperationStatus.Completed.ToString());
    }

    [Fact]
    public async Task DuplicateRequestedMessage_AfterCompletion_IsIgnored()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);
        await harness.DepositAsync(accountId, 12m);

        var requested = harness.Database.OutboxMessages.Single(m => m.Message.Type == nameof(DepositRequested)).Message;
        await harness.Pipeline.DispatchAsync(requested, CancellationToken.None);
        await harness.Pipeline.DrainAsync();

        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(12m));
    }
}
