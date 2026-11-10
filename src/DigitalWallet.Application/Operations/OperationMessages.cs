namespace DigitalWallet.Application.Operations;

// Step messages. "Requested" asks for the checks; "Created" moves the money; "Rejected" is a terminal business failure.

public sealed record DepositRequested(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record DepositCreated(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record DepositRejected(Guid OperationId, Guid AccountId, string Reason);

public sealed record WithdrawalRequested(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record WithdrawalCreated(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record WithdrawalRejected(Guid OperationId, Guid AccountId, string Reason);

public sealed record TransferRequested(Guid OperationId, Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);

public sealed record TransferCreated(Guid OperationId, Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);

public sealed record TransferRejected(Guid OperationId, Guid SourceAccountId, string Reason);

/// <summary>The part of any "Rejected" payload that a generic consumer needs.</summary>
public sealed record RejectedPayload(Guid OperationId, string Reason);
