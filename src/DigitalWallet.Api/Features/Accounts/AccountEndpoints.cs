using System.Security.Claims;
using DigitalWallet.Api.Auth;
using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Application.Accounts;

namespace DigitalWallet.Api.Features.Accounts;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts");

        // The owner is the caller's "sub" claim. The request body cannot choose it.
        group.MapPost("/", CreateAccountAsync)
            .WithName("CreateAccount")
            .WithIdempotency()
            .Produces<AccountResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListAccountsAsync)
            .WithName("ListAccounts")
            .Produces<AccountListResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetAccountAsync)
            .WithName("GetAccount")
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest? request,
        ClaimsPrincipal user,
        CreateAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        var account = await useCase.ExecuteAsync(
            new CreateAccountCommand(user.GetUserId(), request?.Name),
            cancellationToken);

        return Results.Created($"/accounts/{account.Id}", AccountResponse.From(account));
    }

    // Lists only the caller's accounts, so there is no id to guess and no owner to choose.
    private static async Task<IResult> ListAccountsAsync(
        int? limit,
        ClaimsPrincipal user,
        ListAccountsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var accounts = await useCase.ExecuteAsync(new ListAccountsQuery(user.GetUserId(), limit), cancellationToken);

        return Results.Ok(new AccountListResponse(accounts.Select(AccountResponse.From).ToList()));
    }

    private static async Task<IResult> GetAccountAsync(
        Guid id,
        ClaimsPrincipal user,
        GetAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        var account = await useCase.ExecuteAsync(id, cancellationToken);
        AccountOwnership.EnsureOwned(account, user);

        return Results.Ok(AccountResponse.From(account));
    }
}

/// <summary>Optional name for the new account. Omit the body to create an account without a name.</summary>
public sealed record CreateAccountRequest(string? Name);

public sealed record AccountResponse(
    Guid Id,
    Guid OwnerId,
    string? Name,
    string Status,
    decimal Balance,
    string Currency,
    DateTimeOffset CreatedAt)
{
    public static AccountResponse From(AccountView account) => new(
        account.Id,
        account.OwnerId,
        account.Name,
        account.Status,
        account.Balance,
        account.Currency,
        account.CreatedAt);
}

public sealed record AccountListResponse(IReadOnlyList<AccountResponse> Items);
