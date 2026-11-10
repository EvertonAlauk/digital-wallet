using System.Security.Claims;
using DigitalWallet.Api.Auth;
using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Statements;

namespace DigitalWallet.Api.Features.Statements;

public static class StatementEndpoints
{
    public static IEndpointRouteBuilder MapStatementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Statements");

        group.MapGet("/{id:guid}/statement", GetStatementAsync)
            .WithName("GetStatement")
            .Produces<StatementResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // Cursor pagination: pass nextCursor back as ?cursor= to read the next (older) page.
    private static async Task<IResult> GetStatementAsync(
        Guid id,
        string? cursor,
        int? limit,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        GetStatementUseCase useCase,
        CancellationToken cancellationToken)
    {
        await AccountOwnership.EnsureOwnedAsync(id, user, accounts, cancellationToken);

        var page = await useCase.ExecuteAsync(new GetStatementQuery(id, cursor, limit), cancellationToken);

        return Results.Ok(StatementResponse.From(page));
    }
}

public sealed record StatementItemResponse(
    Guid TransactionId,
    string Type,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record StatementResponse(IReadOnlyList<StatementItemResponse> Items, string? NextCursor)
{
    public static StatementResponse From(StatementPage page) => new(
        page.Items
            .Select(item => new StatementItemResponse(item.TransactionId, item.Type, item.Amount, item.Currency, item.CreatedAt))
            .ToList(),
        page.NextCursor);
}
