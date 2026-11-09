namespace DigitalWallet.Domain.Exceptions;

public sealed class CurrencyMismatchException(string expected, string actual)
    : DomainException($"Cannot mix currencies: expected {expected}, received {actual}.")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}
