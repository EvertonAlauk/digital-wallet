namespace DigitalWallet.Application.Exceptions;

/// <summary>
/// Raised when an entity was changed by another operation after this operation read it
/// (optimistic concurrency). The operation can be retried after reloading the entity.
/// </summary>
public sealed class ConcurrencyConflictException(string entityName, Guid entityId)
    : Exception($"{entityName} {entityId} was changed by another operation. Reload it and try again.")
{
    public string EntityName { get; } = entityName;

    public Guid EntityId { get; } = entityId;
}
