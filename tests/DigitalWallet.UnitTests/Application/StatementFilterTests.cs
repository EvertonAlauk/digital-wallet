using DigitalWallet.Application.Common;
using DigitalWallet.Domain.Enums;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class StatementFilterTests
{
    private static readonly DateOnly Day0 = DateOnly.FromDateTime(WalletHarness.Now.UtcDateTime);

    /// <summary>
    /// An account with one deposit (day 0), one withdrawal (day 1) and one transfer out (day 2).
    /// </summary>
    private static async Task<(WalletHarness Harness, Guid Account)> SetupAsync()
    {
        var harness = await WalletHarness.CreateAsync();
        var owner = Guid.NewGuid();
        var account = await harness.CreateFundedAccountAsync(100m, owner);
        var other = await harness.CreateAccountAsync(Guid.NewGuid());

        harness.Clock.Advance(TimeSpan.FromDays(1));
        await harness.WithdrawAsync(account, 30m);

        harness.Clock.Advance(TimeSpan.FromDays(1));
        await harness.TransferAsync(account, other.Id, 10m);

        return (harness, account);
    }

    [Fact]
    public async Task FilterByType_ReturnsOnlyThatKind()
    {
        var (harness, account) = await SetupAsync();

        var deposits = await harness.StatementAsync(account, type: TransactionType.Deposit);
        var withdrawals = await harness.StatementAsync(account, type: TransactionType.Withdrawal);
        var transfers = await harness.StatementAsync(account, type: TransactionType.Transfer);

        deposits.Items.Should().OnlyContain(item => item.Type == "Deposit").And.HaveCount(1);
        withdrawals.Items.Should().OnlyContain(item => item.Type == "Withdrawal").And.HaveCount(1);
        transfers.Items.Should().OnlyContain(item => item.Type == "Transfer").And.HaveCount(1);
    }

    [Fact]
    public async Task NoFilter_ReturnsEveryKind()
    {
        var (harness, account) = await SetupAsync();

        var page = await harness.StatementAsync(account);

        page.Items.Select(item => item.Type).Should().Contain(new[] { "Deposit", "Withdrawal", "Transfer" });
    }

    [Fact]
    public async Task FilterByDate_IsInclusiveOfBothDays()
    {
        var (harness, account) = await SetupAsync();

        // Day 0 (the deposit) through day 1 (the withdrawal), both inclusive. Day 2 (the transfer) is out.
        var page = await harness.StatementAsync(account, from: Day0, to: Day0.AddDays(1));

        page.Items.Select(item => item.Type).Should().BeEquivalentTo(new[] { "Deposit", "Withdrawal" });
    }

    [Fact]
    public async Task FilterByASingleDay_ReturnsOnlyEntriesOfThatDay()
    {
        var (harness, account) = await SetupAsync();

        var page = await harness.StatementAsync(account, from: Day0.AddDays(1), to: Day0.AddDays(1));

        page.Items.Should().ContainSingle().Which.Type.Should().Be("Withdrawal");
    }

    [Fact]
    public async Task TypeAndDateCanBeCombined()
    {
        var (harness, account) = await SetupAsync();

        var page = await harness.StatementAsync(account, type: TransactionType.Deposit, from: Day0.AddDays(1), to: Day0.AddDays(2));

        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task StartDateAfterEndDate_IsRejected()
    {
        var (harness, account) = await SetupAsync();

        Func<Task> act = () => harness.StatementAsync(account, from: Day0.AddDays(2), to: Day0);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("from");
    }

    [Fact]
    public async Task Pages_KeepTheFilterAcrossCursors()
    {
        var harness = await WalletHarness.CreateAsync();
        var account = await harness.CreateFundedAccountAsync(0m);
        for (var index = 0; index < 3; index++)
        {
            await harness.DepositAsync(account, 10m);
            await harness.WithdrawAsync(account, 1m);
        }

        var first = await harness.StatementAsync(account, limit: 2, type: TransactionType.Deposit);
        var second = await harness.StatementAsync(account, cursor: first.NextCursor, limit: 2, type: TransactionType.Deposit);

        first.Items.Should().HaveCount(2).And.OnlyContain(item => item.Type == "Deposit");
        second.Items.Should().HaveCount(1).And.OnlyContain(item => item.Type == "Deposit");
        second.NextCursor.Should().BeNull();
    }
}
