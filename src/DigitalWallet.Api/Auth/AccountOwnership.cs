using System.Security.Claims;
using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Common;

namespace DigitalWallet.Api.Auth;

/// <summary>
/// Object-level authorization (BOLA protection). A customer can act only on accounts whose owner is their "sub".
/// Accounts of other users are reported as not found, so the API does not reveal which account ids exist.
/// </summary>
public static class AccountOwnership
{
    public static void EnsureOwned(AccountView account, ClaimsPrincipal user)
    {
        if (account.OwnerId != user.GetUserId())
        {
            throw new AccountNotFoundException(account.Id);
        }
    }

    public static async Task EnsureOwnedAsync(
        Guid accountId,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        CancellationToken cancellationToken)
    {
        var account = await accounts.ExecuteAsync(accountId, cancellationToken);
        EnsureOwned(account, user);
    }
}
