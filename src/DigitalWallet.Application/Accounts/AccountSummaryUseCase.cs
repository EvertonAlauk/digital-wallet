using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Application.Accounts;

public sealed record CurrencyTotal(string Currency, decimal Amount);

/// <summary>
/// What the owner has in total: the balances of all their accounts (active and inactive), summed per currency,
/// and how many accounts are in each state.
/// </summary>
public sealed record AccountSummary(IReadOnlyList<CurrencyTotal> Totals, int ActiveCount, int InactiveCount);

public sealed class AccountSummaryUseCase(IAccountRepository accounts)
{
    public async Task<AccountSummary> ExecuteAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        // No limit: a total that skipped accounts would not be the total.
        var all = await accounts.ListByOwnerAsync(ownerId, status: null, limit: null, offset: 0, cancellationToken);

        var totals = all
            .GroupBy(account => account.Balance.Currency)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CurrencyTotal(group.Key, group.Sum(account => account.Balance.Amount)))
            .ToList();

        var activeCount = all.Count(account => account.Status == AccountStatus.Active);
        return new AccountSummary(totals, activeCount, all.Count - activeCount);
    }
}
