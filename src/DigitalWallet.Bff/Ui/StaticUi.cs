using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace DigitalWallet.Bff.Ui;

/// <summary>
/// Serves the frontend build from the BFF, so the pages and the BFF share one origin: the session cookie stays
/// first-party and no CORS is needed. Disabled unless "Ui:StaticFilesPath" is set. The BFF routes are matched before
/// the fallback, so only paths that no endpoint handles receive the frontend's index.html.
/// </summary>
public static class StaticUi
{
    public const string ConfigurationKey = "Ui:StaticFilesPath";

    /// <summary>Fails at startup when the configured folder does not exist.</summary>
    public static void Validate(IConfiguration configuration)
    {
        var path = configuration[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException($"{ConfigurationKey} points to a folder that does not exist: '{path}'.");
        }
    }

    /// <summary>Adds the static files middleware. Call it before authentication.</summary>
    public static void UseFrontend(WebApplication app)
    {
        var provider = CreateProvider(app.Configuration);
        if (provider is null)
        {
            return;
        }

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
    }

    /// <summary>Maps the fallback to index.html for client-side routes. Call it after the BFF endpoints.</summary>
    public static void MapFrontendFallback(WebApplication app)
    {
        var provider = CreateProvider(app.Configuration);
        if (provider is null)
        {
            return;
        }

        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = provider });
    }

    private static PhysicalFileProvider? CreateProvider(IConfiguration configuration)
    {
        var path = configuration[ConfigurationKey];
        return string.IsNullOrWhiteSpace(path) ? null : new PhysicalFileProvider(Path.GetFullPath(path));
    }
}
