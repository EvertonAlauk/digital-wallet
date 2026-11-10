using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Common;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class AccountStatusAndSummaryTests
{
    [Fact]
    public async Task Deactivate_StoresTheAccountAsBlocked()
    {
        var harness = await WalletHarness.CreateAsync();
        var account = await harness.CreateAccountAsync(Guid.NewGuid(), "Savings");

        var updated = await harness.SetActiveAsync(account.Id, active: false);

        updated.Status.Should().Be(AccountStatus.Blocked.ToString());
        harness.Database.GetAccount(account.Id)!.Status.Should().Be(AccountStatus.Blocked);
    }

    [Fact]
    public async Task Activate_ReturnsAnInactiveAccountToActive()
    {
        var harness = await WalletHarness.CreateAsync();
        var account = await harness.CreateAccountAsync(Guid.NewGuid());
        await harness.SetActiveAsync(account.Id, active: false);

        var updated = await harness.SetActiveAsync(account.Id, active: true);

        updated.Status.Should().Be(AccountStatus.Active.ToString());
    }

    [Fact]
    public async Task Activate_AnActiveAccount_IsANoOp()
    {
        var harness = await WalletHarness.CreateAsync();
        var account = await harness.CreateAccountAsync(Guid.NewGuid());
        var versionBefore = harness.Database.GetAccount(account.Id)!.Version;

        await harness.SetActiveAsync(account.Id, active: true);

        harness.Database.GetAccount(account.Id)!.Version.Should().Be(versionBefore);
    }

    [Fact]
    public async Task Deactivate_UnknownAccount_ThrowsNotFound()
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.SetActiveAsync(Guid.NewGuid(), active: false);

        await act.Should().ThrowAsync<AccountNotFoundException>();
    }

    [Fact]
    public async Task Rename_KeepsTheBalanceAndTheStatus()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(40m);
        await harness.SetActiveAsync(accountId, active: false);

        var renamed = await harness.RenameAsync(accountId, "Renamed");

        renamed.Name.Should().Be("Renamed");
        renamed.Balance.Should().Be(40m);
        renamed.Status.Should().Be(AccountStatus.Blocked.ToString());
    }

    [Fact]
    public async Task ListByStatus_ReturnsOnlyAccountsInThatState()
    {
        var harness = await WalletHarness.CreateAsync();
        var owner = Guid.NewGuid();
        var active = await harness.CreateAccountAsync(owner, "Active one");
        var inactive = await harness.CreateAccountAsync(owner, "Inactive one");
        await harness.SetActiveAsync(inactive.Id, active: false);

        (await harness.ListAccountsAsync(owner, status: AccountStatus.Active))
            .Select(account => account.Id).Should().Equal(active.Id);
        (await harness.ListAccountsAsync(owner, status: AccountStatus.Blocked))
            .Select(account => account.Id).Should().Equal(inactive.Id);
        (await harness.ListAccountsAsync(owner)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Summary_SumsAllAccountsIncludingInactive_AndCountsEachState()
    {
        var harness = await WalletHarness.CreateAsync();
        var owner = Guid.NewGuid();
        var first = await harness.CreateFundedAccountAsync(100m, owner);
        var second = await harness.CreateFundedAccountAsync(50.25m, owner);
        await harness.SetActiveAsync(second, active: false);

        var summary = await harness.SummaryAsync(owner);

        summary.Totals.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CurrencyTotal(Money.DefaultCurrency, 150.25m));
        summary.ActiveCount.Should().Be(1);
        summary.InactiveCount.Should().Be(1);
        first.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Summary_WithNoAccounts_HasNoTotals()
    {
        var harness = await WalletHarness.CreateAsync();

        var summary = await harness.SummaryAsync(Guid.NewGuid());

        summary.Totals.Should().BeEmpty();
        summary.ActiveCount.Should().Be(0);
        summary.InactiveCount.Should().Be(0);
    }

    [Fact]
    public async Task Summary_DoesNotCountAnotherOwnersAccounts()
    {
        var harness = await WalletHarness.CreateAsync();
        var owner = Guid.NewGuid();
        await harness.CreateFundedAccountAsync(10m, Guid.NewGuid());

        var summary = await harness.SummaryAsync(owner);

        summary.ActiveCount.Should().Be(0);
    }
}
