using DigitalWallet.Api.Auth;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Application.Accounts;

namespace DigitalWallet.Api.Features.Admin;

/// <summary>Support endpoints. Everything under /api/admin requires the admin role.</summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        group.MapGet("/accounts/{id:guid}", GetAnyAccountAsync)
            .WithName("AdminGetAccount")
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // Admins read any account, but cannot move money on someone else's behalf: movements have no admin path.
    private static async Task<IResult> GetAnyAccountAsync(
        Guid id,
        GetAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        var account = await useCase.ExecuteAsync(id, cancellationToken);

        return Results.Ok(AccountResponse.From(account));
    }
}
