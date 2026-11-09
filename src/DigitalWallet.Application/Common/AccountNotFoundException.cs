namespace DigitalWallet.Application.Common;

public sealed class AccountNotFoundException(Guid accountId)
    : Exception($"Account {accountId} was not found.")
{
    public Guid AccountId { get; } = accountId;
}
