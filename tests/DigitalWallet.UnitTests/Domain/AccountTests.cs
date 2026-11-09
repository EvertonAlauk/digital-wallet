using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Domain;

public class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Account OpenAccount(decimal initialBalance = 0m)
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);

        if (initialBalance != 0m)
        {
            account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(initialBalance), Guid.NewGuid()));
        }

        return account;
    }

    [Fact]
    public void Open_StartsActiveWithZeroBalanceAndVersionZero()
    {
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var account = Account.Open(id, ownerId, Now);

        account.Id.Should().Be(id);
        account.OwnerId.Should().Be(ownerId);
        account.CreatedAt.Should().Be(Now);
        account.Status.Should().Be(AccountStatus.Active);
        account.Balance.Should().Be(Money.Brl(0m));
        account.Version.Should().Be(0);
    }

    [Fact]
    public void ApplyEntry_Credit_IncreasesBalanceAndVersion()
    {
        var account = OpenAccount();

        account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(100m), Guid.NewGuid()));

        account.Balance.Should().Be(Money.Brl(100m));
        account.Version.Should().Be(1);
    }

    [Fact]
    public void ApplyEntry_Debit_DecreasesBalance()
    {
        var account = OpenAccount(100m);

        account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(-30m), Guid.NewGuid()));

        account.Balance.Should().Be(Money.Brl(70m));
    }

    [Fact]
    public void ApplyEntry_DebitExactBalance_LeavesZero()
    {
        var account = OpenAccount(50m);

        account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(-50m), Guid.NewGuid()));

        account.Balance.Should().Be(Money.Brl(0m));
    }

    [Fact]
    public void ApplyEntry_DebitMoreThanBalance_ThrowsAndLeavesStateUnchanged()
    {
        var account = OpenAccount(50m);
        var versionBefore = account.Version;

        Action act = () => account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(-50.01m), Guid.NewGuid()));

        act.Should().Throw<InsufficientFundsException>();
        account.Balance.Should().Be(Money.Brl(50m));
        account.Version.Should().Be(versionBefore);
    }

    [Fact]
    public void ApplyEntry_CreditToBlockedAccount_Throws()
    {
        var account = OpenAccount();
        account.Block();

        Action act = () => account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(10m), Guid.NewGuid()));

        act.Should().Throw<AccountBlockedException>();
        account.Balance.Should().Be(Money.Brl(0m));
    }

    [Fact]
    public void ApplyEntry_DebitFromBlockedAccount_Throws()
    {
        var account = OpenAccount(100m);
        account.Block();

        Action act = () => account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(-10m), Guid.NewGuid()));

        act.Should().Throw<AccountBlockedException>();
        account.Balance.Should().Be(Money.Brl(100m));
    }

    [Fact]
    public void Block_ChangesStatusAndIncrementsVersion()
    {
        var account = OpenAccount();
        var versionBefore = account.Version;

        account.Block();

        account.Status.Should().Be(AccountStatus.Blocked);
        account.Version.Should().Be(versionBefore + 1);
    }

    [Fact]
    public void Block_WhenAlreadyBlocked_DoesNotChangeVersion()
    {
        var account = OpenAccount();
        account.Block();
        var versionBefore = account.Version;

        account.Block();

        account.Version.Should().Be(versionBefore);
    }

    [Fact]
    public void Rehydrate_RestoresPersistedState()
    {
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var account = Account.Rehydrate(id, ownerId, Money.Brl(42m), AccountStatus.Blocked, 7, Now);

        account.Id.Should().Be(id);
        account.OwnerId.Should().Be(ownerId);
        account.Balance.Should().Be(Money.Brl(42m));
        account.Status.Should().Be(AccountStatus.Blocked);
        account.Version.Should().Be(7);
        account.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Unblock_AllowsMovementsAgain()
    {
        var account = OpenAccount();
        account.Block();
        account.Unblock();

        account.ApplyEntry(new LedgerEntry(account.Id, Money.Brl(10m), Guid.NewGuid()));

        account.Status.Should().Be(AccountStatus.Active);
        account.Balance.Should().Be(Money.Brl(10m));
    }

    [Fact]
    public void ApplyEntry_EntryForAnotherAccount_Throws()
    {
        var account = OpenAccount();

        Action act = () => account.ApplyEntry(new LedgerEntry(Guid.NewGuid(), Money.Brl(10m), Guid.NewGuid()));

        act.Should().Throw<InvalidTransactionException>();
        account.Balance.Should().Be(Money.Brl(0m));
    }

    [Fact]
    public void ApplyEntry_DifferentCurrency_Throws()
    {
        var account = OpenAccount();

        Action act = () => account.ApplyEntry(new LedgerEntry(account.Id, new Money(10m, "USD"), Guid.NewGuid()));

        act.Should().Throw<CurrencyMismatchException>();
    }
}
