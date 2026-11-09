namespace DigitalWallet.Domain.Exceptions;

public sealed class InvalidAmountException(string message) : DomainException(message);
