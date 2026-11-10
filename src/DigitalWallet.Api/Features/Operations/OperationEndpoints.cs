using System.Security.Claims;
using DigitalWallet.Api.Auth;
using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Operations;

namespace DigitalWallet.Api.Features.Operations;

public static class OperationEndpoints
{
    public static IEndpointRouteBuilder MapOperationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/operations").WithTags("Operations");

        group.MapGet("/{id:guid}", GetOperationAsync)
            .WithName("GetOperation")
            .Produces<OperationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // An operation is visible to the owner of its account. For anyone else it does not exist.
    private static async Task<IResult> GetOperationAsync(
        Guid id,
        ClaimsPrincipal user,
        GetOperationUseCase useCase,
        GetAccountUseCase accounts,
        CancellationToken cancellationToken)
    {
        var operation = await useCase.ExecuteAsync(id, cancellationToken);
        await AccountOwnership.EnsureOwnedAsync(operation.AccountId, user, accounts, cancellationToken);

        return Results.Ok(OperationResponse.From(operation));
    }
}

public sealed record OperationAcceptedResponse(Guid OperationId, string Status)
{
    /// <summary>202 with the location of the operation, where the outcome can be read.</summary>
    public static IResult ToResult(OperationAccepted accepted) =>
        Results.Accepted(
            $"/operations/{accepted.OperationId}",
            new OperationAcceptedResponse(accepted.OperationId, "Accepted"));
}

public sealed record OperationResponse(
    Guid Id,
    Guid AccountId,
    string Kind,
    string Status,
    string? FailureReason,
    Guid? TransactionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static OperationResponse From(OperationView operation) => new(
        operation.Id,
        operation.AccountId,
        operation.Kind,
        operation.Status,
        operation.FailureReason,
        operation.TransactionId,
        operation.CreatedAt,
        operation.CompletedAt);
}
