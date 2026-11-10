namespace DigitalWallet.Domain.Exceptions;

/// <summary>The account name does not satisfy the naming rules (length or characters).</summary>
public sealed class InvalidAccountNameException(string message) : DomainException(message);
