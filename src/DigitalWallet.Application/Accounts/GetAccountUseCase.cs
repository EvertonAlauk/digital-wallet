using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;

namespace DigitalWallet.Application.Accounts;

public sealed class GetAccountUseCase(IAccountRepository accounts)
{
    public async Task<AccountView> ExecuteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(accountId, cancellationToken)
            ?? throw new AccountNotFoundException(accountId);

        return AccountView.From(account);
    }
}
