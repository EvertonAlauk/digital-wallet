namespace DigitalWallet.Domain.Exceptions;

/// <summary>Base class for all business rule violations in the domain.</summary>
public abstract class DomainException(string message) : Exception(message);
