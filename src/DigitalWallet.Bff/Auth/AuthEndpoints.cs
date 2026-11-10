using DigitalWallet.Bff.Keycloak;

namespace DigitalWallet.Bff.Auth;

/// <summary>
/// The frontend's own login screen posts the credentials here. The BFF exchanges them with Keycloak and returns the
/// tokens. The BFF stores nothing: no session, no cookie, no user table.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        // Login and refresh are the attack surface for password guessing, so they are rate limited per client address.
        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .RequireRateLimiting(AuthRateLimits.PolicyName)
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .RequireRateLimiting(AuthRateLimits.PolicyName)
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        KeycloakClient keycloak,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "The username and the password are required.");
        }

        var result = await keycloak.PasswordGrantAsync(request.Username, request.Password, cancellationToken);
        return ToHttpResult(result, rejectedDetail: "Invalid username or password.");
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        KeycloakClient keycloak,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "The refresh token is required.");
        }

        var result = await keycloak.RefreshAsync(request.RefreshToken, cancellationToken);
        return ToHttpResult(result, rejectedDetail: "The session has expired. Sign in again.");
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request,
        KeycloakClient keycloak,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "The refresh token is required.");
        }

        await keycloak.LogoutAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static IResult ToHttpResult(KeycloakResult result, string rejectedDetail) => result.Outcome switch
    {
        KeycloakOutcome.Success => Results.Ok(result.Tokens),
        KeycloakOutcome.Rejected => Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed",
            detail: rejectedDetail),
        KeycloakOutcome.Misconfigured => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Authentication is misconfigured",
            detail: "The BFF could not authenticate with the identity provider."),
        _ => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Identity provider unavailable",
            detail: "The identity provider could not be reached. Try again."),
    };
}

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public static class AuthRateLimits
{
    public const string PolicyName = "auth";

    public const int PermitLimit = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
