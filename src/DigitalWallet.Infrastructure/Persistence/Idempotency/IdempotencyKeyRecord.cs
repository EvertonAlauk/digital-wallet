namespace DigitalWallet.Infrastructure.Persistence.Idempotency;

/// <summary>Database row for an idempotency key. Persistence model only; the application uses IdempotencyEntry.</summary>
internal sealed class IdempotencyKeyRecord
{
    public string Key { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsCompleted { get; set; }

    public int? ResponseStatusCode { get; set; }

    public string? ResponseLocation { get; set; }

    public string? ResponseContentType { get; set; }

    public byte[]? ResponseBody { get; set; }
}
