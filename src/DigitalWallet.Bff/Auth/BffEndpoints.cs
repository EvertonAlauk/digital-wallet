using DigitalWallet.Bff.Identity;
using DigitalWallet.Bff.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace DigitalWallet.Bff.Auth;

/// <summary>
/// The endpoints the frontend uses. Tokens never leave the BFF: the browser gets a session cookie and the user's profile.
/// </summary>
public static class BffEndpoints
{
    public static IEndpointRouteBuilder MapBffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bff").WithTags("BFF");

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .AddEndpointFilter<CsrfFilter>()
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/session", SessionAsync)
            .WithName("Session")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.General)
            .Produces<SessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.General)
            .AddEndpointFilter<CsrfFilter>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        IIdentityService identity,
        SessionTokenProvider sessions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "The username and the password are required.");
        }

        var result = await identity.PasswordSignInAsync(request.Username, request.Password, cancellationToken);
        if (result.Outcome == IdentityOutcome.Rejected)
        {
            // Wrong password and locked account get the same answer, so the response does not reveal which it is.
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed",
                detail: "Usuário ou senha inválidos");
        }

        if (result.Outcome == IdentityOutcome.Misconfigured)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Authentication is misconfigured",
                detail: "The BFF could not authenticate with the identity provider.");
        }

        if (result.Outcome != IdentityOutcome.Success || result.Tokens is not { } tokens)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Identity provider unavailable",
                detail: "The identity provider could not be reached. Try again.");
        }

        var user = SessionUser.FromAccessToken(tokens.AccessToken);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Identity provider error",
                detail: "The identity provider returned a token that cannot be read.");
        }

        // The previous session of this browser was ended by LoginSessionRotation, before authentication ran.
        var sessionId = Guid.NewGuid().ToString("N");
        await http.SignInAsync(
            SessionAuthentication.Scheme,
            SessionTickets.CreatePrincipal(sessionId, user),
            SessionTickets.CreateProperties(tokens, DateTimeOffset.UtcNow));

        return Results.Ok(new LoginResponse(user));
    }

    private static IResult SessionAsync(HttpContext http) =>
        SessionUser.FromPrincipal(http.User) is { } user
            ? Results.Ok(new SessionResponse(user))
            : Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Not signed in",
                detail: "There is no active session.");

    /// <summary>Idempotent: a missing or expired session still ends with 204 and no cookie.</summary>
    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        ITicketStore store,
        SessionTokenProvider sessions,
        IIdentityService identity,
        CancellationToken cancellationToken)
    {
        if (http.User.FindFirst(SessionClaims.SessionId)?.Value is { } sessionId)
        {
            var ticket = await store.RetrieveAsync(sessionId);
            var refreshToken = ticket?.Properties.GetTokenValue(SessionTickets.RefreshToken);

            await sessions.EndSessionAsync(sessionId, cancellationToken);
            if (refreshToken is not null)
            {
                await identity.SignOutAsync(refreshToken, cancellationToken);
            }
        }

        await http.SignOutAsync(SessionAuthentication.Scheme);
        return Results.NoContent();
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(SessionUser User);

public sealed record SessionResponse(SessionUser User);
