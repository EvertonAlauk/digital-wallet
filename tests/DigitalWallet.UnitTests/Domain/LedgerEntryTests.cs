using System.Reflection;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Domain;

public class LedgerEntryTests
{
    [Fact]
    public void Constructor_ZeroAmount_Throws()
    {
        Action act = () => _ = new LedgerEntry(Guid.NewGuid(), Money.Brl(0m), Guid.NewGuid());

        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void Constructor_NegativeAmount_IsAllowedAsDebit()
    {
        var accountId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var entry = new LedgerEntry(accountId, Money.Brl(-5m), transactionId);

        entry.AccountId.Should().Be(accountId);
        entry.Amount.Should().Be(Money.Brl(-5m));
        entry.TransactionId.Should().Be(transactionId);
    }

    [Fact]
    public void Properties_AreReadOnly()
    {
        var properties = typeof(LedgerEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        properties.Should().NotBeEmpty();
        properties.Should().OnlyContain(p => !p.CanWrite);
    }
}
