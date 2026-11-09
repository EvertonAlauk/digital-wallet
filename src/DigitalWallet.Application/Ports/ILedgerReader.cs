using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Application.Ports;

/// <summary>Read-only access to the ledger, used by statements. Never changes data.</summary>
public interface ILedgerReader
{
    /// <summary>
    /// Returns up to <paramref name="limit"/> entries of an account, most recent first.
    /// When <paramref name="beforeSequence"/> is set, only entries with a smaller sequence are returned.
    /// </summary>
    Task<IReadOnlyList<LedgerRecord>> ReadAccountEntriesAsync(
        Guid accountId,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>A ledger entry as shown in a statement. Sequence is the persistence order, used for cursors.</summary>
public sealed record LedgerRecord(
    long Sequence,
    Guid TransactionId,
    TransactionType Type,
    Money Amount,
    DateTimeOffset CreatedAt);
