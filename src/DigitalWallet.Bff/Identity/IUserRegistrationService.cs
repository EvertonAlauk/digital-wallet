namespace DigitalWallet.Bff.Identity;

/// <summary>Creates user accounts in the identity provider. Keycloak is the only implementation; tests use a fake.</summary>
public interface IUserRegistrationService
{
    Task<RegistrationResult> RegisterAsync(NewUserAccount account, CancellationToken cancellationToken);
}

/// <summary>The data needed to create an account. The password is held only for the duration of the call.</summary>
public sealed record NewUserAccount(string Email, string Password, string FirstName, string LastName)
{
    /// <summary>Hides the password, so a logged record cannot leak it.</summary>
    public override string ToString() => $"NewUserAccount({Email})";
}

public enum RegistrationOutcome
{
    Created,

    /// <summary>An account with this email already exists.</summary>
    AlreadyExists,

    /// <summary>The identity provider refused the data, for example a password that does not meet its policy.</summary>
    Rejected,

    /// <summary>The identity provider could not be reached or did not answer in time.</summary>
    Unavailable,

    /// <summary>The BFF's registrar credentials are wrong or lack permission: a server-side configuration error.</summary>
    Misconfigured,
}

public sealed record RegistrationResult(RegistrationOutcome Outcome);
