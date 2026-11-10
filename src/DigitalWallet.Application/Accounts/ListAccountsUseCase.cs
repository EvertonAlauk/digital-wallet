using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;

namespace DigitalWallet.Application.Accounts;

public sealed record ListAccountsQuery(Guid OwnerId, int? Limit);

/// <summary>Lists the customer accounts of one owner, newest first. Each account keeps its own balance and status.</summary>
public sealed class ListAccountsUseCase(IAccountRepository accounts)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public async Task<IReadOnlyList<AccountView>> ExecuteAsync(ListAccountsQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
        {
            throw new RequestValidationException("limit", $"The limit must be between 1 and {MaxLimit}.");
        }

        var found = await accounts.ListByOwnerAsync(query.OwnerId, limit, cancellationToken);
        return found.Select(AccountView.From).ToList();
    }
}
