using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace DigitalWallet.Bff.Ui;

/// <summary>
/// Serves the frontend build from the BFF, so the pages and the BFF share one origin: the session cookie stays
/// first-party and no CORS is needed. Disabled unless "Ui:StaticFilesPath" is set. The BFF routes are matched before
/// the fallback, so only paths that no endpoint handles reach the frontend.
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

    /// <summary>Adds the static files middleware (the CSS, JavaScript and images). Call it before authentication.</summary>
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

    /// <summary>
    /// Maps every other path to its page. A static export writes one HTML file per page (login.html for /login), so
    /// the page's own file is served when it exists. Otherwise the app's index.html is served, so client-side routes work.
    /// </summary>
    public static void MapFrontendFallback(WebApplication app)
    {
        var provider = CreateProvider(app.Configuration);
        if (provider is null)
        {
            return;
        }

        app.MapFallback((HttpContext context) =>
        {
            var file = PageFileFor(context.Request.Path.Value ?? "/", provider) ?? provider.GetFileInfo("index.html");
            return Results.File(file.PhysicalPath!, "text/html; charset=utf-8");
        });
    }

    /// <summary>The page file for a path such as /login, or null when the path names no page (root, folders, files).</summary>
    private static IFileInfo? PageFileFor(string path, IFileProvider provider)
    {
        if (path == "/" || path.EndsWith('/') || Path.GetExtension(path).Length > 0)
        {
            return null;
        }

        var file = provider.GetFileInfo(path.TrimStart('/') + ".html");
        return file.Exists ? file : null;
    }

    private static PhysicalFileProvider? CreateProvider(IConfiguration configuration)
    {
        var path = configuration[ConfigurationKey];
        return string.IsNullOrWhiteSpace(path) ? null : new PhysicalFileProvider(Path.GetFullPath(path));
    }
}
