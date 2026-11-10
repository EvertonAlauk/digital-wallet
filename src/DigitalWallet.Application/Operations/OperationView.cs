using DigitalWallet.Domain.Entities;

namespace DigitalWallet.Application.Operations;

public sealed record OperationView(
    Guid Id,
    string Kind,
    string Status,
    string? FailureReason,
    Guid? TransactionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static OperationView From(Operation operation) => new(
        operation.Id,
        operation.Kind.ToString(),
        operation.Status.ToString(),
        operation.FailureReason,
        operation.TransactionId,
        operation.CreatedAt,
        operation.CompletedAt);
}
