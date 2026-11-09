using DigitalWallet.Application.Common;
using DigitalWallet.Application.Ports;

namespace DigitalWallet.Application.Statements;

public sealed record GetStatementQuery(Guid AccountId, string? Cursor, int? Limit);

public sealed record StatementItem(
    Guid TransactionId,
    string Type,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

/// <summary>A page of statement entries, most recent first. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record StatementPage(IReadOnlyList<StatementItem> Items, string? NextCursor);

public sealed class GetStatementUseCase(IAccountRepository accounts, ILedgerReader ledger)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public async Task<StatementPage> ExecuteAsync(GetStatementQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
        {
            throw new RequestValidationException("limit", $"The limit must be between 1 and {MaxLimit}.");
        }

        var beforeSequence = query.Cursor is null ? (long?)null : StatementCursor.Decode(query.Cursor);

        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken)
            ?? throw new AccountNotFoundException(query.AccountId);

        // Read one extra entry to know whether another page exists without a second query.
        var records = await ledger.ReadAccountEntriesAsync(account.Id, beforeSequence, limit + 1, cancellationToken);

        var hasMore = records.Count > limit;
        var page = records.Take(limit).ToList();

        var items = page
            .Select(record => new StatementItem(
                record.TransactionId,
                record.Type.ToString(),
                record.Amount.Amount,
                record.Amount.Currency,
                record.CreatedAt))
            .ToList();

        var nextCursor = hasMore ? StatementCursor.Encode(page[^1].Sequence) : null;

        return new StatementPage(items, nextCursor);
    }
}
