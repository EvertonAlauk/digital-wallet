using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Brl_UsesDefaultCurrency()
    {
        var money = Money.Brl(10m);

        money.Currency.Should().Be("BRL");
        money.Amount.Should().Be(10m);
    }

    [Fact]
    public void Constructor_NormalizesCurrencyToUpperCase()
    {
        var money = new Money(1m, "brl");

        money.Currency.Should().Be("BRL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("BRLX")]
    [InlineData("B1L")]
    public void Constructor_InvalidCurrencyCode_Throws(string currency)
    {
        Action act = () => _ = new Money(1m, currency);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_SameCurrency_SumsAmounts()
    {
        var result = Money.Brl(10m).Add(Money.Brl(2.5m));

        result.Should().Be(Money.Brl(12.5m));
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var result = Money.Brl(10m).Subtract(Money.Brl(4m));

        result.Should().Be(Money.Brl(6m));
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        var usd = new Money(1m, "USD");

        Action act = () => Money.Brl(1m).Add(usd);

        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void Subtract_DifferentCurrency_Throws()
    {
        var usd = new Money(1m, "USD");

        Action act = () => Money.Brl(1m).Subtract(usd);

        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void Negate_FlipsSign()
    {
        Money.Brl(7m).Negate().Should().Be(Money.Brl(-7m));
    }

    [Fact]
    public void Decimal_DoesNotHaveFloatingPointErrors()
    {
        var result = Money.Brl(0.1m).Add(Money.Brl(0.2m));

        result.Should().Be(Money.Brl(0.3m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void EnsurePositive_ZeroOrNegative_Throws(decimal amount)
    {
        Action act = () => Money.Brl(amount).EnsurePositive();

        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void EnsurePositive_PositiveAmount_DoesNotThrow()
    {
        Action act = () => Money.Brl(0.01m).EnsurePositive();

        act.Should().NotThrow();
    }
}
