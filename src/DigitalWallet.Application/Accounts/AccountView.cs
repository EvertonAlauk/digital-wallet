using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Accounts;

public sealed record AccountView(
    Guid Id,
    Guid OwnerId,
    string? Name,
    string Status,
    decimal Balance,
    string Currency,
    DateTimeOffset CreatedAt)
{
    public static AccountView From(Account account) => new(
        account.Id,
        account.OwnerId,
        account.Name,
        account.Status.ToString(),
        account.Balance.Amount,
        account.Balance.Currency,
        account.CreatedAt);
}
