namespace DigitalWallet.Domain.Exceptions;

public sealed class InsufficientFundsException(Guid accountId, decimal balance, decimal requested)
    : DomainException($"Insufficient funds in account {accountId}: balance {balance}, requested {requested}.")
{
    public Guid AccountId { get; } = accountId;
}
