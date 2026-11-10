using DigitalWallet.Bff.Auth;
using DigitalWallet.Bff.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace DigitalWallet.Bff.Proxy;

/// <summary>
/// Forwards /api/* to DigitalWallet.Api. The browser never sends a token: the proxy removes the session cookie and adds
/// the access token of the session as a Bearer token. Every request needs a valid session and the X-CSRF header.
/// </summary>
public static class ApiProxy
{
    public const string ClusterName = "api";

    public const string Prefix = "/api";

    /// <summary>The admin routes keep their /api prefix in the API, so they are not rewritten.</summary>
    public static IReadOnlyList<RouteConfig> Routes() =>
    [
        new RouteConfig
        {
            RouteId = "api-admin",
            ClusterId = ClusterName,
            Order = 1,
            Match = new RouteMatch { Path = "/api/admin/{**catch-all}" },
        },
        new RouteConfig
        {
            RouteId = "api",
            ClusterId = ClusterName,
            Order = 2,
            Match = new RouteMatch { Path = "/api/{**catch-all}" },
            Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = Prefix }],
        },
    ];

    public static IReadOnlyList<ClusterConfig> Clusters(string apiBaseUrl) =>
    [
        new ClusterConfig
        {
            ClusterId = ClusterName,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["api"] = new DestinationConfig { Address = apiBaseUrl },
            },
        },
    ];

    /// <summary>Maps the proxy. It runs the session and CSRF checks before anything is forwarded.</summary>
    public static void MapApiProxy(this IEndpointRouteBuilder app) =>
        app.MapReverseProxy(pipeline => pipeline.Use(EnforceSessionAsync))
            .RequireRateLimiting(RateLimitPolicies.General);

    private static async Task EnforceSessionAsync(HttpContext context, Func<Task> next)
    {
        if (!context.Request.Headers.TryGetValue(CsrfFilter.HeaderName, out var header) || (string?)header != "1")
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "CSRF check failed",
                detail: "The X-CSRF: 1 header is required.").ExecuteAsync(context);
            return;
        }

        var sessionId = context.User.FindFirst(SessionClaims.SessionId)?.Value;
        if (sessionId is null)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Not signed in",
                detail: "There is no active session.").ExecuteAsync(context);
            return;
        }

        // Returns a fresh token, refreshing it first when it is about to expire. Null means the session has ended.
        var accessToken = await context.RequestServices.GetRequiredService<SessionTokenProvider>()
            .GetAccessTokenAsync(sessionId, context.RequestAborted);
        if (accessToken is null)
        {
            await context.SignOutAsync(SessionAuthentication.Scheme);
            await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Session expired",
                detail: "The session has ended. Sign in again.").ExecuteAsync(context);
            return;
        }

        // The browser's credentials stop here: the cookie and any Authorization header it sent are replaced.
        context.Request.Headers.Remove("Cookie");
        context.Request.Headers["Authorization"] = $"Bearer {accessToken}";

        await next();
    }
}
