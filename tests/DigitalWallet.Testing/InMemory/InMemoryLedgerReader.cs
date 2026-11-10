using DigitalWallet.Application.Ports;

namespace DigitalWallet.Testing.InMemory;

/// <summary>Ledger reader over <see cref="InMemoryDatabase"/>, used by statement tests and the test API factory.</summary>
public sealed class InMemoryLedgerReader(InMemoryDatabase database) : ILedgerReader
{
    public Task<IReadOnlyList<LedgerRecord>> ReadAccountEntriesAsync(
        Guid accountId,
        long? beforeSequence,
        int limit,
        LedgerFilter filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.ReadAccountEntries(accountId, beforeSequence, limit, filter));
    }
}
