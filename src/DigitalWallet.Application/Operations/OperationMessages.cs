namespace DigitalWallet.Application.Operations;

// Pipeline messages. "Requested" asks for the checks; "Created" moves the money. Outcomes are recorded on the operation.

public sealed record DepositRequested(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record DepositCreated(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record WithdrawalRequested(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record WithdrawalCreated(Guid OperationId, Guid AccountId, decimal Amount);

public sealed record TransferRequested(Guid OperationId, Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);

public sealed record TransferCreated(Guid OperationId, Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);
