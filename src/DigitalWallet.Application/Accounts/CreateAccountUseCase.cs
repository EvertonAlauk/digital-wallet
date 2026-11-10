using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Accounts;

/// <summary>Opens a customer account. The name is optional; blank names are stored as no name.</summary>
public sealed record CreateAccountCommand(Guid OwnerId, string? Name = null);

public sealed class CreateAccountUseCase(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<AccountView> ExecuteAsync(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        if (command.OwnerId == Guid.Empty)
        {
            throw new RequestValidationException("ownerId", "The owner id is required.");
        }

        // Account.Open validates the name and throws InvalidAccountNameException, which the API maps to 422.
        var account = Account.Open(Guid.NewGuid(), command.OwnerId, clock.UtcNow, command.Name);

        await accounts.AddAsync(account, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return AccountView.From(account);
    }
}
