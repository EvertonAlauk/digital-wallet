namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>Reads and sends the session cookie the way a browser does. Tests disable the client's cookie handling.</summary>
public static class SessionCookie
{
    public const string Name = "__Host-wallet";

    /// <summary>The Set-Cookie header that sets the session cookie, or null when the response sets none.</summary>
    public static string? SetCookieOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(Name + "=", StringComparison.Ordinal))
            : null;

    /// <summary>The cookie value a browser sends back on the next request.</summary>
    public static string ValueOf(string setCookie) => setCookie.Split(';')[0][(Name.Length + 1)..];

    public static void Attach(HttpRequestMessage request, string value) =>
        request.Headers.Add("Cookie", $"{Name}={value}");
}
