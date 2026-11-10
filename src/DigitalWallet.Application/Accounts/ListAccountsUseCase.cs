using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Application.Accounts;

/// <summary>Filters the owner's accounts by status (null means all) and selects one page of them.</summary>
public sealed record ListAccountsQuery(Guid OwnerId, AccountStatus? Status, int? Limit, int? Offset = null);

/// <summary>One page of accounts. <see cref="TotalCount"/> counts every account that matches the filter.</summary>
public sealed record AccountPage(IReadOnlyList<AccountView> Items, int TotalCount, int Limit, int Offset);

/// <summary>Lists the customer accounts of one owner, newest first, one page at a time.</summary>
public sealed class ListAccountsUseCase(IAccountRepository accounts)
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 100;

    public async Task<AccountPage> ExecuteAsync(ListAccountsQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
        {
            throw new RequestValidationException("limit", $"The limit must be between 1 and {MaxLimit}.");
        }

        var offset = query.Offset ?? 0;
        if (offset < 0)
        {
            throw new RequestValidationException("offset", "The offset cannot be negative.");
        }

        var found = await accounts.ListByOwnerAsync(query.OwnerId, query.Status, limit, offset, cancellationToken);
        var total = await accounts.CountByOwnerAsync(query.OwnerId, query.Status, cancellationToken);

        return new AccountPage(found.Select(AccountView.From).ToList(), total, limit, offset);
    }
}
