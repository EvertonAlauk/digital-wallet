using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Domain.Entities;

/// <summary>
/// A deposit, withdrawal or transfer requested by a client. The request is accepted immediately and processed in
/// the background; the operation records the outcome. Only the Created step changes balances, and it completes or
/// rejects the operation in the same commit.
/// </summary>
public sealed class Operation
{
    private Operation(Guid id, OperationKind kind, Guid accountId, Guid? destinationAccountId, decimal amount, DateTimeOffset createdAt)
    {
        // Positive amounts are a business rule, enforced where the operation is created.
        Money.Brl(amount).EnsurePositive();

        Id = id;
        Kind = kind;
        AccountId = accountId;
        DestinationAccountId = destinationAccountId;
        Amount = amount;
        CreatedAt = createdAt;
        Status = OperationStatus.Accepted;
    }

    // Used by EF Core to materialize entities.
    private Operation()
    {
    }

    public Guid Id { get; }

    public OperationKind Kind { get; }

    /// <summary>The customer account (source account for transfers).</summary>
    public Guid AccountId { get; }

    /// <summary>The destination account, for transfers only.</summary>
    public Guid? DestinationAccountId { get; }

    public decimal Amount { get; }

    public DateTimeOffset CreatedAt { get; }

    public OperationStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public Guid? TransactionId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Restores an operation from persisted state, without re-running the acceptance rules.</summary>
    public static Operation Rehydrate(
        Guid id,
        OperationKind kind,
        Guid accountId,
        Guid? destinationAccountId,
        decimal amount,
        DateTimeOffset createdAt,
        OperationStatus status,
        string? failureReason,
        Guid? transactionId,
        DateTimeOffset? completedAt)
    {
        var operation = new Operation(id, kind, accountId, destinationAccountId, amount, createdAt)
        {
            Status = status,
            FailureReason = failureReason,
            TransactionId = transactionId,
            CompletedAt = completedAt,
        };

        return operation;
    }

    public static Operation Accept(
        Guid id,
        OperationKind kind,
        Guid accountId,
        Guid? destinationAccountId,
        decimal amount,
        DateTimeOffset createdAt) =>
        new(id, kind, accountId, destinationAccountId, amount, createdAt);

    public void Complete(Guid transactionId, DateTimeOffset completedAt)
    {
        EnsureAccepted();

        Status = OperationStatus.Completed;
        TransactionId = transactionId;
        CompletedAt = completedAt;
    }

    public void Reject(string reason, DateTimeOffset rejectedAt)
    {
        EnsureAccepted();

        Status = OperationStatus.Rejected;
        FailureReason = reason;
        CompletedAt = rejectedAt;
    }

    private void EnsureAccepted()
    {
        if (Status != OperationStatus.Accepted)
        {
            throw new InvalidTransactionException($"The operation is already {Status}.");
        }
    }
}
