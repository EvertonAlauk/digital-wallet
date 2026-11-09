namespace DigitalWallet.Application.Ports;

/// <summary>
/// Atomic commit of everything changed in the current operation.
/// Either all changes are persisted, or none are. Throws
/// <see cref="Exceptions.ConcurrencyConflictException"/> when a changed account was modified by someone else
/// after it was read; in that case nothing is persisted.
/// </summary>
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}
