namespace DigitalWallet.Application.Ports;

/// <summary>
/// Atomic commit of everything changed in the current operation.
/// Either all changes are persisted, or none are. Throws
/// <see cref="Exceptions.ConcurrencyConflictException"/> when a changed account was modified by someone else
/// after it was read; in that case nothing is persisted.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Opens a single database transaction that spans the locking reads and <see cref="CommitAsync"/>.
    /// Needed by operations that lock rows (transfers). Operations that only use the version check do not call it.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);
}
