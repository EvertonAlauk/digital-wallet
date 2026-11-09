using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Accounts;

public sealed record CreateAccountCommand(Guid OwnerId);

public sealed class CreateAccountUseCase(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<AccountView> ExecuteAsync(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        if (command.OwnerId == Guid.Empty)
        {
            throw new RequestValidationException("ownerId", "The owner id is required.");
        }

        var account = Account.Open(Guid.NewGuid(), command.OwnerId, clock.UtcNow);

        await accounts.AddAsync(account, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return AccountView.From(account);
    }
}
