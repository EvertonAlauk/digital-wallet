using System.Security.Claims;
using DigitalWallet.Api.Auth;
using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Common;
using DigitalWallet.Application.Statements;
using DigitalWallet.Domain.Enums;

namespace DigitalWallet.Api.Features.Statements;

public static class StatementEndpoints
{
    public static IEndpointRouteBuilder MapStatementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Statements");

        group.MapGet("/{id:guid}/statement", GetStatementAsync)
            .WithName("GetStatement")
            .Produces<StatementResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // Cursor pagination: pass nextCursor back as ?cursor= to read the next (older) page. The filters (type, from, to)
    // apply to every page, so the same filters must be sent with the cursor.
    private static async Task<IResult> GetStatementAsync(
        Guid id,
        string? cursor,
        int? limit,
        string? type,
        string? from,
        string? to,
        ClaimsPrincipal user,
        GetAccountUseCase accounts,
        GetStatementUseCase useCase,
        CancellationToken cancellationToken)
    {
        await AccountOwnership.EnsureOwnedAsync(id, user, accounts, cancellationToken);

        var page = await useCase.ExecuteAsync(
            new GetStatementQuery(id, cursor, limit, ParseType(type), ParseDate("from", from), ParseDate("to", to)),
            cancellationToken);

        return Results.Ok(StatementResponse.From(page));
    }

    /// <summary>
    /// Reads a calendar day in yyyy-MM-dd. Minimal API would silently drop a malformed DateOnly? parameter, so the
    /// value is read here and refused when it is not a date.
    /// </summary>
    private static DateOnly? ParseDate(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new RequestValidationException(field, "The date must be in the format yyyy-MM-dd.");
    }

    private static TransactionType? ParseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        if (Enum.TryParse<TransactionType>(type, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new RequestValidationException("type", "The type must be Deposit, Withdrawal, Transfer or Reversal.");
    }
}

public sealed record StatementItemResponse(
    Guid TransactionId,
    string Type,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record StatementResponse(IReadOnlyList<StatementItemResponse> Items, string? NextCursor)
{
    public static StatementResponse From(StatementPage page) => new(
        page.Items
            .Select(item => new StatementItemResponse(item.TransactionId, item.Type, item.Amount, item.Currency, item.CreatedAt))
            .ToList(),
        page.NextCursor);
}
