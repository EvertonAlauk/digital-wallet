namespace DigitalWallet.Domain.Exceptions;

public sealed class InvalidTransactionException(string message) : DomainException(message);
