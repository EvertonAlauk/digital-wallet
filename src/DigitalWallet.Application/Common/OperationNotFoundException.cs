namespace DigitalWallet.Application.Common;

public sealed class OperationNotFoundException(Guid operationId)
    : Exception($"Operation {operationId} was not found.")
{
    public Guid OperationId { get; } = operationId;
}
