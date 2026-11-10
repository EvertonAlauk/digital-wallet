namespace DigitalWallet.Bff.Sessions;

/// <summary>Claim names of the session principal. The roles use <see cref="System.Security.Claims.ClaimTypes.Role"/>.</summary>
public static class SessionClaims
{
    /// <summary>The session identifier. It is also the key of the ticket in the store.</summary>
    public const string SessionId = "bff_sid";

    public const string Subject = "sub";

    public const string Username = "preferred_username";

    public const string Name = "name";

    public const string Email = "email";
}
