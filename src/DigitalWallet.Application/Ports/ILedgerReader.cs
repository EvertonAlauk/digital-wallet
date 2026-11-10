using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Application.Ports;

/// <summary>Read-only access to the ledger, used by statements. Never changes data.</summary>
public interface ILedgerReader
{
    /// <summary>
    /// Returns up to <paramref name="limit"/> entries of an account that match <paramref name="filter"/>, most recent first.
    /// When <paramref name="beforeSequence"/> is set, only entries with a smaller sequence are returned.
    /// </summary>
    Task<IReadOnlyList<LedgerRecord>> ReadAccountEntriesAsync(
        Guid accountId,
        long? beforeSequence,
        int limit,
        LedgerFilter filter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Narrows a statement. <see cref="From"/> is inclusive and <see cref="To"/> is exclusive, both in UTC.
/// Null fields do not filter.
/// </summary>
public sealed record LedgerFilter(TransactionType? Type = null, DateTimeOffset? From = null, DateTimeOffset? To = null)
{
    public static LedgerFilter None { get; } = new();
}

/// <summary>A ledger entry as shown in a statement. Sequence is the persistence order, used for cursors.</summary>
public sealed record LedgerRecord(
    long Sequence,
    Guid TransactionId,
    TransactionType Type,
    Money Amount,
    DateTimeOffset CreatedAt);
