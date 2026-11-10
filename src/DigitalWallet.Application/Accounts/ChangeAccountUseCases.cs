using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;

namespace DigitalWallet.Application.Accounts;

public sealed record SetAccountActiveCommand(Guid AccountId, bool Active);

public sealed record RenameAccountCommand(Guid AccountId, string? Name);

/// <summary>
/// Activates or deactivates an account. An inactive account is stored as Blocked, so it refuses every movement
/// but its balance and statement stay readable. Changing the state locks the account row first, so it cannot
/// interleave with a movement that is being applied.
/// </summary>
public sealed class SetAccountActiveUseCase(IAccountRepository accounts, IUnitOfWork unitOfWork)
{
    public async Task<AccountView> ExecuteAsync(SetAccountActiveCommand command, CancellationToken cancellationToken)
    {
        var locked = await accounts.GetByIdsForUpdateAsync([command.AccountId], cancellationToken);
        var account = locked[command.AccountId];

        if (command.Active)
        {
            account.Unblock();
        }
        else
        {
            account.Block();
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return AccountView.From(account);
    }
}

/// <summary>Changes the name of an account. Blank clears it. The name rules are in the domain.</summary>
public sealed class RenameAccountUseCase(IAccountRepository accounts, IUnitOfWork unitOfWork)
{
    public async Task<AccountView> ExecuteAsync(RenameAccountCommand command, CancellationToken cancellationToken)
    {
        var locked = await accounts.GetByIdsForUpdateAsync([command.AccountId], cancellationToken);
        var account = locked[command.AccountId];

        account.Rename(command.Name);

        await unitOfWork.CommitAsync(cancellationToken);
        return AccountView.From(account);
    }
}
