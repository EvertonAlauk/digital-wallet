namespace DigitalWallet.Domain.Exceptions;

public sealed class AccountBlockedException(Guid accountId)
    : DomainException($"Account {accountId} is blocked and does not accept movements.")
{
    public Guid AccountId { get; } = accountId;
}
