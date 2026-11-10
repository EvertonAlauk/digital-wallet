using System.Diagnostics.CodeAnalysis;

namespace DigitalWallet.Bff.Common;

public static class HttpUrl
{
    /// <summary>
    /// True only for an absolute http or https URL. Uri.TryCreate alone is platform dependent: on Linux it accepts a
    /// path such as "/token" as a file URI, so the scheme is checked explicitly.
    /// </summary>
    public static bool IsAbsoluteHttp([NotNullWhen(true)] string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
