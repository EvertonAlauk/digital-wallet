using System.Text.RegularExpressions;
using DigitalWallet.Bff.Identity;

namespace DigitalWallet.Bff.Auth;

/// <summary>
/// Checks the registration form before Keycloak is called. The email is the username, so it is stored in lower case.
/// The password rules here are a floor: the identity provider may apply stricter ones and refuses them with a rejection.
/// </summary>
public static class RegistrationRules
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxNameLength = 60;
    public const int MaxEmailLength = 254;

    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Returns the account to create, or the field errors when the form is not valid.</summary>
    public static (NewUserAccount? Account, IReadOnlyDictionary<string, string[]> Errors) Check(
        string? email,
        string? password,
        string? firstName,
        string? lastName)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var first = firstName?.Trim() ?? string.Empty;
        var last = lastName?.Trim() ?? string.Empty;

        if (normalizedEmail.Length == 0 || normalizedEmail.Length > MaxEmailLength || !EmailPattern.IsMatch(normalizedEmail))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            errors["password"] = [$"The password must have between {MinPasswordLength} and {MaxPasswordLength} characters."];
        }

        if (first.Length == 0 || first.Length > MaxNameLength)
        {
            errors["firstName"] = [$"Enter a first name of up to {MaxNameLength} characters."];
        }

        if (last.Length == 0 || last.Length > MaxNameLength)
        {
            errors["lastName"] = [$"Enter a last name of up to {MaxNameLength} characters."];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new NewUserAccount(normalizedEmail, password!, first, last), errors);
    }
}
