using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Application.Transfers;

namespace DigitalWallet.Api.Features.Transfers;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transfers").WithTags("Transfers");

        group.MapPost("/", TransferAsync)
            .WithName("Transfer")
            .WithIdempotency()
            .Produces<TransferResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> TransferAsync(
        TransferRequest request,
        TransferUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new TransferCommand(request.SourceAccountId, request.DestinationAccountId, request.Amount),
            cancellationToken);

        return Results.Created($"/accounts/{request.SourceAccountId}", TransferResponse.From(result));
    }
}

public sealed record TransferRequest(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);

public sealed record TransferResponse(Guid TransactionId, decimal SourceBalance, decimal DestinationBalance)
{
    public static TransferResponse From(TransferResult result) =>
        new(result.TransactionId, result.SourceBalance, result.DestinationBalance);
}
