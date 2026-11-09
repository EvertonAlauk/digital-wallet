using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Domain;

public class TransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_BalancedTwoLegs_Succeeds()
    {
        var debitAccount = Guid.NewGuid();
        var creditAccount = Guid.NewGuid();
        var id = Guid.NewGuid();

        var transaction = Transaction.Create(id, TransactionType.Transfer, Now, new[]
        {
            (debitAccount, Money.Brl(-10m)),
            (creditAccount, Money.Brl(10m)),
        });

        transaction.Id.Should().Be(id);
        transaction.Type.Should().Be(TransactionType.Transfer);
        transaction.Status.Should().Be(TransactionStatus.Completed);
        transaction.CreatedAt.Should().Be(Now);
        transaction.Entries.Should().HaveCount(2);
        transaction.Entries.Should().OnlyContain(e => e.TransactionId == id);
    }

    [Fact]
    public void Create_MultiLegBalanced_Succeeds()
    {
        var transaction = Transaction.Create(Guid.NewGuid(), TransactionType.Transfer, Now, new[]
        {
            (Guid.NewGuid(), Money.Brl(-30m)),
            (Guid.NewGuid(), Money.Brl(10m)),
            (Guid.NewGuid(), Money.Brl(20m)),
        });

        transaction.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Create_UnbalancedLegs_Throws()
    {
        Action act = () => Transaction.Create(Guid.NewGuid(), TransactionType.Transfer, Now, new[]
        {
            (Guid.NewGuid(), Money.Brl(-10m)),
            (Guid.NewGuid(), Money.Brl(9.99m)),
        });

        act.Should().Throw<UnbalancedTransactionException>();
    }

    [Fact]
    public void Create_SingleLeg_Throws()
    {
        Action act = () => Transaction.Create(Guid.NewGuid(), TransactionType.Deposit, Now, new[]
        {
            (Guid.NewGuid(), Money.Brl(10m)),
        });

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Create_ZeroAmountLeg_Throws()
    {
        Action act = () => Transaction.Create(Guid.NewGuid(), TransactionType.Transfer, Now, new[]
        {
            (Guid.NewGuid(), Money.Brl(0m)),
            (Guid.NewGuid(), Money.Brl(0m)),
        });

        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void Create_MixedCurrencies_Throws()
    {
        Action act = () => Transaction.Create(Guid.NewGuid(), TransactionType.Transfer, Now, new[]
        {
            (Guid.NewGuid(), Money.Brl(-10m)),
            (Guid.NewGuid(), new Money(10m, "USD")),
        });

        act.Should().Throw<CurrencyMismatchException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Transfer_NonPositiveAmount_Throws(decimal amount)
    {
        Action act = () => Transaction.Transfer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Money.Brl(amount), Now);

        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void Transfer_SameAccount_Throws()
    {
        var accountId = Guid.NewGuid();

        Action act = () => Transaction.Transfer(Guid.NewGuid(), accountId, accountId, Money.Brl(10m), Now);

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Transfer_CreatesDebitAndCreditOfSameAmount()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();

        var transaction = Transaction.Transfer(Guid.NewGuid(), from, to, Money.Brl(25m), Now);

        transaction.Type.Should().Be(TransactionType.Transfer);
        transaction.Entries.Should().ContainSingle(e => e.AccountId == from && e.Amount == Money.Brl(-25m));
        transaction.Entries.Should().ContainSingle(e => e.AccountId == to && e.Amount == Money.Brl(25m));
    }

    [Fact]
    public void Transfer_AppliedToBothAccounts_PreservesTotalMoney()
    {
        var origin = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        var destination = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        origin.ApplyEntry(new LedgerEntry(origin.Id, Money.Brl(100m), Guid.NewGuid()));

        var transaction = Transaction.Transfer(Guid.NewGuid(), origin.Id, destination.Id, Money.Brl(40m), Now);
        foreach (var entry in transaction.Entries)
        {
            var target = entry.AccountId == origin.Id ? origin : destination;
            target.ApplyEntry(entry);
        }

        origin.Balance.Should().Be(Money.Brl(60m));
        destination.Balance.Should().Be(Money.Brl(40m));
        (origin.Balance.Amount + destination.Balance.Amount).Should().Be(100m);
    }

    [Fact]
    public void MarkAsReversed_CompletedTransaction_BecomesReversed()
    {
        var transaction = Transaction.Transfer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Money.Brl(5m), Now);

        transaction.MarkAsReversed();

        transaction.Status.Should().Be(TransactionStatus.Reversed);
        transaction.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void MarkAsReversed_AlreadyReversed_Throws()
    {
        var transaction = Transaction.Transfer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Money.Brl(5m), Now);
        transaction.MarkAsReversed();

        Action act = transaction.MarkAsReversed;

        act.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void MarkAsReversed_ReversalTransaction_Throws()
    {
        var reversal = Transaction.Create(Guid.NewGuid(), TransactionType.Reversal, Now, new[]
        {
            (Guid.NewGuid(), Money.Brl(-5m)),
            (Guid.NewGuid(), Money.Brl(5m)),
        });

        Action act = reversal.MarkAsReversed;

        act.Should().Throw<InvalidTransactionException>();
    }
}
