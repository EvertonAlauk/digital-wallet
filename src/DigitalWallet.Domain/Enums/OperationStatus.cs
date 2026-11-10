namespace DigitalWallet.Domain.Enums;

/// <summary>
/// Lifecycle of a client request. Accepted is the only state that can change: it becomes Completed when the
/// money moves, or Rejected when a business rule stops it. Both final states are terminal.
/// </summary>
public enum OperationStatus
{
    Accepted,
    Completed,
    Rejected,
}
