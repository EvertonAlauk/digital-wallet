using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Domain;

public class SettlementAndMovementTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SettlementAccount_CanHaveNegativeBalance()
    {
        var settlement = Account.OpenSettlement(Guid.NewGuid(), Now);

        settlement.ApplyEntry(new LedgerEntry(settlement.Id, Money.Brl(-500m), Guid.NewGuid()));

        settlement.Kind.Should().Be(AccountKind.Settlement);
        settlement.Balance.Should().Be(Money.Brl(-500m));
    }

    [Fact]
    public void CustomerAccount_OpenedByDefault()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);

        account.Kind.Should().Be(AccountKind.Customer);
    }

    [Fact]
    public void Deposit_CreditsCustomerAndDebitsSettlement()
    {
        var customer = Guid.NewGuid();
        var settlement = Guid.NewGuid();

        var transaction = Transaction.Deposit(Guid.NewGuid(), customer, settlement, Money.Brl(80m), Now);

        transaction.Type.Should().Be(TransactionType.Deposit);
        transaction.Entries.Should().ContainSingle(e => e.AccountId == customer && e.Amount == Money.Brl(80m));
        transaction.Entries.Should().ContainSingle(e => e.AccountId == settlement && e.Amount == Money.Brl(-80m));
    }

    [Fact]
    public void Withdrawal_DebitsCustomerAndCreditsSettlement()
    {
        var customer = Guid.NewGuid();
        var settlement = Guid.NewGuid();

        var transaction = Transaction.Withdrawal(Guid.NewGuid(), customer, settlement, Money.Brl(80m), Now);

        transaction.Type.Should().Be(TransactionType.Withdrawal);
        transaction.Entries.Should().ContainSingle(e => e.AccountId == customer && e.Amount == Money.Brl(-80m));
        transaction.Entries.Should().ContainSingle(e => e.AccountId == settlement && e.Amount == Money.Brl(80m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Deposit_NonPositiveAmount_Throws(decimal amount)
    {
        Action act = () => Transaction.Deposit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Money.Brl(amount), Now);

        act.Should().Throw<InvalidAmountException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Withdrawal_NonPositiveAmount_Throws(decimal amount)
    {
        Action act = () => Transaction.Withdrawal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Money.Brl(amount), Now);

        act.Should().Throw<InvalidAmountException>();
    }
}
