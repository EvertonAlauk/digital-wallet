using System.Security.Claims;
using DigitalWallet.Api.Auth;
using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Operations;

namespace DigitalWallet.Api.Features.Movements;

public static class MovementEndpoints
{
    public static IEndpointRouteBuilder MapMovementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Movements");

        group.MapPost("/{id:guid}/deposits", DepositAsync)
            .WithName("Deposit")
            .WithIdempotency()
            .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/withdrawals", WithdrawAsync)
            .WithName("Withdraw")
            .WithIdempotency()
            .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    // The request is accepted (202). The balance changes later, in the background; GET /operations/{id} shows the outcome.
    private static async Task<IResult> DepositAsync(
        Guid id,
        MovementRequest request,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        AcceptDepositUseCase useCase,
        CancellationToken cancellationToken)
    {
        await AccountOwnership.EnsureOwnedAsync(id, user, accounts, cancellationToken);
        var accepted = await useCase.ExecuteAsync(new AcceptDepositCommand(id, request.Amount), cancellationToken);

        return OperationAcceptedResponse.ToResult(accepted);
    }

    private static async Task<IResult> WithdrawAsync(
        Guid id,
        MovementRequest request,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        AcceptWithdrawalUseCase useCase,
        CancellationToken cancellationToken)
    {
        await AccountOwnership.EnsureOwnedAsync(id, user, accounts, cancellationToken);
        var accepted = await useCase.ExecuteAsync(new AcceptWithdrawalCommand(id, request.Amount), cancellationToken);

        return OperationAcceptedResponse.ToResult(accepted);
    }
}

public sealed record MovementRequest(decimal Amount);
