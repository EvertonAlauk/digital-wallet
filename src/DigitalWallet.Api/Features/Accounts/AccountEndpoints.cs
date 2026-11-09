using DigitalWallet.Application.Accounts;

namespace DigitalWallet.Api.Features.Accounts;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts");

        group.MapPost("/", CreateAccountAsync)
            .WithName("CreateAccount")
            .Produces<AccountResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAccountAsync)
            .WithName("GetAccount")
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest request,
        CreateAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        var account = await useCase.ExecuteAsync(new CreateAccountCommand(request.OwnerId), cancellationToken);

        return Results.Created($"/accounts/{account.Id}", AccountResponse.From(account));
    }

    private static async Task<IResult> GetAccountAsync(
        Guid id,
        GetAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        var account = await useCase.ExecuteAsync(id, cancellationToken);

        return Results.Ok(AccountResponse.From(account));
    }
}

public sealed record CreateAccountRequest(Guid OwnerId);

public sealed record AccountResponse(
    Guid Id,
    Guid OwnerId,
    string Status,
    decimal Balance,
    string Currency,
    DateTimeOffset CreatedAt)
{
    public static AccountResponse From(AccountView account) => new(
        account.Id,
        account.OwnerId,
        account.Status,
        account.Balance,
        account.Currency,
        account.CreatedAt);
}
