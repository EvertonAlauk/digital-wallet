using DigitalWallet.Domain.Exceptions;

namespace DigitalWallet.Domain.ValueObjects;

/// <summary>
/// Monetary amount with a currency. Immutable. Values may be negative (e.g. a debit in a ledger entry);
/// the "positive value" rule is enforced by the operations that require it (EnsurePositive).
/// </summary>
public readonly record struct Money
{
    public const string DefaultCurrency = "BRL";

    public Money(decimal amount, string currency)
    {
        // ISO 4217 code with 3 letters. An invalid argument is a programming error, hence ArgumentException.
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("The currency must be an ISO code of 3 letters.", nameof(currency));
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public static Money Brl(decimal amount) => new(amount, DefaultCurrency);

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Negate() => new(-Amount, Currency);

    public void EnsurePositive()
    {
        if (!IsPositive)
        {
            throw new InvalidAmountException($"The amount must be positive. Received: {Amount}.");
        }
    }

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }
    }

    public override string ToString() => $"{Currency} {Amount:0.00}";
}
