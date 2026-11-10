using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Exceptions;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Domain;

public class AccountNameTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_WithoutAName_HasNoName()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);

        account.Name.Should().BeNull();
    }

    [Fact]
    public void Open_TrimsTheName()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, "  Holiday fund  ");

        account.Name.Should().Be("Holiday fund");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_BlankName_IsStoredAsNoName(string name)
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, name);

        account.Name.Should().BeNull();
    }

    [Fact]
    public void Open_NameAtTheMaximumLength_IsAccepted()
    {
        var name = new string('a', Account.MaxNameLength);

        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, name);

        account.Name.Should().Be(name);
    }

    [Fact]
    public void Open_NameLongerThanTheMaximum_IsRejected()
    {
        var name = new string('a', Account.MaxNameLength + 1);

        Action act = () => Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, name);

        act.Should().Throw<InvalidAccountNameException>().WithMessage("*60*");
    }

    [Theory]
    [InlineData("Savings\nnow")]
    [InlineData("Savings\u0000")]
    public void Open_NameWithControlCharacters_IsRejected(string name)
    {
        Action act = () => Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, name);

        act.Should().Throw<InvalidAccountNameException>();
    }

    [Fact]
    public void Rehydrate_KeepsTheStoredName()
    {
        var account = Account.Rehydrate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DigitalWallet.Domain.ValueObjects.Money.Zero(),
            DigitalWallet.Domain.Enums.AccountStatus.Active,
            0,
            Now,
            name: "Stored");

        account.Name.Should().Be("Stored");
    }

    [Fact]
    public void NameDoesNotChangeTheBalanceOrTheStatus()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, "Named");

        account.Balance.Amount.Should().Be(0m);
        account.Status.Should().Be(DigitalWallet.Domain.Enums.AccountStatus.Active);
    }
}

public class AccountRenameTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rename_ChangesTheNameAndIncrementsTheVersion()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, "Old");
        var before = account.Version;

        account.Rename("  New name ");

        account.Name.Should().Be("New name");
        account.Version.Should().Be(before + 1);
    }

    [Fact]
    public void Rename_ToTheSameName_DoesNotChangeTheVersion()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, "Same");
        var before = account.Version;

        account.Rename("Same");

        account.Version.Should().Be(before);
    }

    [Fact]
    public void Rename_ToBlank_ClearsTheName()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, "Named");

        account.Rename("   ");

        account.Name.Should().BeNull();
    }

    [Fact]
    public void Rename_TooLong_IsRejectedAndKeepsTheName()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, "Kept");

        Action act = () => account.Rename(new string('x', Account.MaxNameLength + 1));

        act.Should().Throw<DigitalWallet.Domain.Exceptions.InvalidAccountNameException>();
        account.Name.Should().Be("Kept");
    }

    [Fact]
    public void Rename_DoesNotChangeTheStatus()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now, "Named");
        account.Block();

        account.Rename("Renamed");

        account.Status.Should().Be(DigitalWallet.Domain.Enums.AccountStatus.Blocked);
    }
}
