namespace DigitalWallet.Bff.Auth;

/// <summary>Names of the rate limit policies. Each is partitioned by client address.</summary>
public static class RateLimitPolicies
{
    /// <summary>Every /bff endpoint, except the login.</summary>
    public const string General = "bff";

    /// <summary>The login: the endpoint most exposed to password guessing, so its limit is the strictest.</summary>
    public const string Login = "bff-login";
}
