using System.Security.Claims;
using DigitalWallet.Api.Auth;
using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Operations;

namespace DigitalWallet.Api.Features.Transfers;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transfers").WithTags("Transfers");

        group.MapPost("/", TransferAsync)
            .WithName("Transfer")
            .WithIdempotency()
            .Produces<OperationAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> TransferAsync(
        TransferRequest request,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        AcceptTransferUseCase useCase,
        CancellationToken cancellationToken)
    {
        // Only the source account must belong to the caller. The destination can be any account.
        await AccountOwnership.EnsureOwnedAsync(request.SourceAccountId, user, accounts, cancellationToken);

        var accepted = await useCase.ExecuteAsync(
            new AcceptTransferCommand(request.SourceAccountId, request.DestinationAccountId, request.Amount),
            cancellationToken);

        return OperationAcceptedResponse.ToResult(accepted);
    }
}

public sealed record TransferRequest(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);
