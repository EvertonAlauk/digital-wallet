using DigitalWallet.Application.Common;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class MovementUseCaseTests
{
    [Fact]
    public async Task Deposit_CreditsCustomerAndDebitsSettlementInOneTransaction()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        var result = await harness.DepositAsync(accountId, 100m);

        result.Type.Should().Be(TransactionType.Deposit.ToString());
        result.AccountBalance.Should().Be(100m);
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(100m));
        harness.Database.GetAccount(SettlementAccount.Id)!.Balance.Should().Be(Money.Brl(-100m));

        var transaction = harness.Database.GetTransaction(result.TransactionId);
        transaction.Should().NotBeNull();
        transaction!.Entries.Should().HaveCount(2);
        transaction.Entries.Should().ContainSingle(entry => entry.AccountId == accountId && entry.Amount == Money.Brl(100m));
        transaction.Entries.Should().ContainSingle(entry => entry.AccountId == SettlementAccount.Id && entry.Amount == Money.Brl(-100m));
    }

    [Fact]
    public async Task Deposit_UnknownAccount_ThrowsNotFoundAndPersistsNothing()
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.DepositAsync(Guid.NewGuid(), 10m);

        await act.Should().ThrowAsync<AccountNotFoundException>();
        harness.Database.TransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task Deposit_MoreThanTwoDecimalPlaces_ThrowsValidation()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        Func<Task> act = () => harness.DepositAsync(accountId, 10.001m);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("amount");
        harness.Database.TransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task Deposit_ZeroAmount_ThrowsBusinessRuleAndPersistsNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        Func<Task> act = () => harness.DepositAsync(accountId, 0m);

        await act.Should().ThrowAsync<InvalidAmountException>();
        harness.Database.TransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task Deposit_BlockedAccount_ThrowsBusinessRuleAndPersistsNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(10m);
        await harness.BlockAccountAsync(accountId);

        Func<Task> act = () => harness.DepositAsync(accountId, 5m);

        await act.Should().ThrowAsync<AccountBlockedException>();
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(10m));
        harness.Database.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Withdrawal_DebitsCustomerAndCreditsSettlement()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(100m);

        var result = await harness.WithdrawAsync(accountId, 40m);

        result.Type.Should().Be(TransactionType.Withdrawal.ToString());
        result.AccountBalance.Should().Be(60m);
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(60m));
        harness.Database.GetAccount(SettlementAccount.Id)!.Balance.Should().Be(Money.Brl(-60m));
    }

    [Fact]
    public async Task Withdrawal_ExactBalance_LeavesZero()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(25m);

        await harness.WithdrawAsync(accountId, 25m);

        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(0m));
    }

    [Fact]
    public async Task Withdrawal_InsufficientFunds_ThrowsAndChangesNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(50m);

        Func<Task> act = () => harness.WithdrawAsync(accountId, 60m);

        await act.Should().ThrowAsync<InsufficientFundsException>();
        harness.Database.GetAccount(accountId)!.Balance.Should().Be(Money.Brl(50m));
        harness.Database.GetAccount(SettlementAccount.Id)!.Balance.Should().Be(Money.Brl(-50m));
        harness.Database.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Withdrawal_UnknownAccount_ThrowsNotFound()
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.WithdrawAsync(Guid.NewGuid(), 1m);

        await act.Should().ThrowAsync<AccountNotFoundException>();
    }
}
