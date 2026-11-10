namespace DigitalWallet.Domain.Events;

/// <summary>Raised when a transfer is committed. Other systems learn about it through the outbox.</summary>
public sealed record TransferCompleted(
    Guid TransactionId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt);
