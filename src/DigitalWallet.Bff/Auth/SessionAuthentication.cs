using DigitalWallet.Bff.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalWallet.Bff.Auth;

/// <summary>
/// The browser's session: a cookie that holds only a reference, and a server-side ticket that holds the tokens.
/// </summary>
public static class SessionAuthentication
{
    public const string Scheme = "BffSession";

    /// <summary>The __Host- prefix makes the browser accept the cookie only with Secure, Path=/ and no Domain.</summary>
    public const string CookieName = "__Host-wallet";

    /// <summary>The session ends after this much inactivity. Each request renews it (sliding expiration).</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>The claim type the cookie handler writes into the cookie (the value of the store key).</summary>
    private const string FrameworkSessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    /// <summary>
    /// Session rotation on login (call before UseAuthentication). The cookie handler reuses the store key of the
    /// session a request arrives with, so a login would keep the old key, and an old cookie would open the new session
    /// (session fixation). Here the previous session is ended and the cookie is hidden from the handler, so the login
    /// gets a new key.
    /// </summary>
    public static IApplicationBuilder UseLoginSessionRotation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path.Equals("/bff/login", StringComparison.OrdinalIgnoreCase)
                && context.Request.Cookies.TryGetValue(CookieName, out var cookie))
            {
                var options = context.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme);
                // The cookie carries the cookie handler's own session claim, whose value is the store key (our session id).
                var previous = options.TicketDataFormat.Unprotect(cookie);
                if (previous?.Principal.FindFirst(FrameworkSessionIdClaim)?.Value is { } previousSessionId)
                {
                    await context.RequestServices.GetRequiredService<SessionTokenProvider>()
                        .EndSessionAsync(previousSessionId, context.RequestAborted);
                }

                context.Request.Headers.Remove("Cookie");
            }

            await next(context);
        });

    public static IServiceCollection AddBffSessions(this IServiceCollection services)
    {
        services.AddSingleton<ITicketStore, InMemoryTicketStore>();
        services.AddSingleton<SessionTokenProvider>();
        services.AddAuthorization();

        services.AddAuthentication(Scheme)
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.Cookie.IsEssential = true;
                options.ExpireTimeSpan = IdleTimeout;
                options.SlidingExpiration = true;

                // The API-style endpoints answer with status codes, not with redirects to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        // The cookie handler keeps the ticket in the store, not in the cookie.
        services.AddOptions<CookieAuthenticationOptions>(Scheme)
            .Configure<ITicketStore>((options, store) => options.SessionStore = store);

        return services;
    }
}
