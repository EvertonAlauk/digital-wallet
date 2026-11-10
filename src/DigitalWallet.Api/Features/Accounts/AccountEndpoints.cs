using System.Security.Claims;
using DigitalWallet.Api.Auth;
using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Common;
using DigitalWallet.Domain.Enums;

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

        group.MapGet("/summary", GetSummaryAsync)
            .WithName("GetAccountSummary")
            .Produces<AccountSummaryResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetAccountAsync)
            .WithName("GetAccount")
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", RenameAccountAsync)
            .WithName("RenameAccount")
            .Produces<AccountResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/deactivate", DeactivateAccountAsync)
            .WithName("DeactivateAccount")
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", ActivateAccountAsync)
            .WithName("ActivateAccount")
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

    // Lists only the caller's accounts, one page at a time. status=active or status=inactive narrows the list.
    private static async Task<IResult> ListAccountsAsync(
        string? status,
        int? limit,
        int? offset,
        ClaimsPrincipal user,
        ListAccountsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var page = await useCase.ExecuteAsync(
            new ListAccountsQuery(user.GetUserId(), ParseStatus(status), limit, offset),
            cancellationToken);

        return Results.Ok(AccountListResponse.From(page));
    }

    private static async Task<IResult> GetSummaryAsync(
        ClaimsPrincipal user,
        AccountSummaryUseCase useCase,
        CancellationToken cancellationToken)
    {
        var summary = await useCase.ExecuteAsync(user.GetUserId(), cancellationToken);

        return Results.Ok(AccountSummaryResponse.From(summary));
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

    private static async Task<IResult> RenameAccountAsync(
        Guid id,
        UpdateAccountRequest request,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        RenameAccountUseCase useCase,
        CancellationToken cancellationToken)
    {
        await AccountOwnership.EnsureOwnedAsync(id, user, accounts, cancellationToken);
        var account = await useCase.ExecuteAsync(new RenameAccountCommand(id, request.Name), cancellationToken);

        return Results.Ok(AccountResponse.From(account));
    }

    private static Task<IResult> DeactivateAccountAsync(
        Guid id,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        SetAccountActiveUseCase useCase,
        CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: false, user, accounts, useCase, cancellationToken);

    private static Task<IResult> ActivateAccountAsync(
        Guid id,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        SetAccountActiveUseCase useCase,
        CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: true, user, accounts, useCase, cancellationToken);

    private static async Task<IResult> SetActiveAsync(
        Guid id,
        bool active,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        SetAccountActiveUseCase useCase,
        CancellationToken cancellationToken)
    {
        await AccountOwnership.EnsureOwnedAsync(id, user, accounts, cancellationToken);
        var account = await useCase.ExecuteAsync(new SetAccountActiveCommand(id, active), cancellationToken);

        return Results.Ok(AccountResponse.From(account));
    }

    /// <summary>Maps the status filter to a domain status. Inactive accounts are stored as Blocked.</summary>
    private static AccountStatus? ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        null or "" => null,
        "active" => AccountStatus.Active,
        "inactive" => AccountStatus.Blocked,
        _ => throw new RequestValidationException("status", "The status must be 'active' or 'inactive'."),
    };
}

/// <summary>Optional name for the new account. Omit the body to create an account without a name.</summary>
public sealed record CreateAccountRequest(string? Name);

/// <summary>New name for the account. Null or blank clears it.</summary>
public sealed record UpdateAccountRequest(string? Name);

/// <summary>
/// An account as the API returns it. Status is "Active" or "Blocked"; "Blocked" is the inactive state shown to the
/// user, where movements are refused until the account is activated again.
/// </summary>
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

/// <summary>One page of accounts. TotalCount is the number of accounts that match the filter, across all pages.</summary>
public sealed record AccountListResponse(IReadOnlyList<AccountResponse> Items, int TotalCount, int Limit, int Offset)
{
    public static AccountListResponse From(AccountPage page) => new(
        page.Items.Select(AccountResponse.From).ToList(),
        page.TotalCount,
        page.Limit,
        page.Offset);
}

public sealed record CurrencyTotalResponse(string Currency, decimal Amount);

public sealed record AccountSummaryResponse(
    IReadOnlyList<CurrencyTotalResponse> Totals,
    int ActiveCount,
    int InactiveCount)
{
    public static AccountSummaryResponse From(AccountSummary summary) => new(
        summary.Totals.Select(total => new CurrencyTotalResponse(total.Currency, total.Amount)).ToList(),
        summary.ActiveCount,
        summary.InactiveCount);
}
