using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Api.Features.Operations;
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
        AcceptTransferUseCase useCase,
        CancellationToken cancellationToken)
    {
        var accepted = await useCase.ExecuteAsync(
            new AcceptTransferCommand(request.SourceAccountId, request.DestinationAccountId, request.Amount),
            cancellationToken);

        return OperationAcceptedResponse.ToResult(accepted);
    }
}

public sealed record TransferRequest(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);
