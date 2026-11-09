using DigitalWallet.Application.Movements;

namespace DigitalWallet.Api.Features.Movements;

public static class MovementEndpoints
{
    public static IEndpointRouteBuilder MapMovementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Movements");

        group.MapPost("/{id:guid}/deposits", DepositAsync)
            .WithName("Deposit")
            .Produces<MovementResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/withdrawals", WithdrawAsync)
            .WithName("Withdraw")
            .Produces<MovementResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> DepositAsync(
        Guid id,
        MovementRequest request,
        DepositUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(new MovementCommand(id, request.Amount), cancellationToken);

        return Results.Created($"/accounts/{id}", MovementResponse.From(result));
    }

    private static async Task<IResult> WithdrawAsync(
        Guid id,
        MovementRequest request,
        WithdrawalUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(new MovementCommand(id, request.Amount), cancellationToken);

        return Results.Created($"/accounts/{id}", MovementResponse.From(result));
    }
}

public sealed record MovementRequest(decimal Amount);

public sealed record MovementResponse(Guid TransactionId, string Type, decimal AccountBalance)
{
    public static MovementResponse From(MovementResult result) =>
        new(result.TransactionId, result.Type, result.AccountBalance);
}
