using DigitalWallet.Bff.Identity;

namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>Stands in for the account creation in the identity provider. Records every account it was asked to create.</summary>
public sealed class FakeUserRegistrationService : IUserRegistrationService
{
    private readonly List<NewUserAccount> _accounts = [];

    public Func<NewUserAccount, RegistrationOutcome> Outcome { get; set; } = _ => RegistrationOutcome.Created;

    public IReadOnlyList<NewUserAccount> Accounts
    {
        get
        {
            lock (_accounts)
            {
                return _accounts.ToList();
            }
        }
    }

    public void Reset()
    {
        lock (_accounts)
        {
            _accounts.Clear();
        }

        Outcome = _ => RegistrationOutcome.Created;
    }

    public Task<RegistrationResult> RegisterAsync(NewUserAccount account, CancellationToken cancellationToken)
    {
        lock (_accounts)
        {
            _accounts.Add(account);
        }

        return Task.FromResult(new RegistrationResult(Outcome(account)));
    }
}
