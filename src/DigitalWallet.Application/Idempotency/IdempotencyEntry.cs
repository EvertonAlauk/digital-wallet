namespace DigitalWallet.Application.Idempotency;

/// <summary>
/// State stored for one Idempotency-Key. <see cref="Response"/> is null while the first request is still processing.
/// </summary>
public sealed record IdempotencyEntry(
    string Key,
    string RequestHash,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    StoredResponse? Response);

/// <summary>The successful response that is replayed for a repeated request.</summary>
public sealed record StoredResponse(int StatusCode, string? Location, string? ContentType, byte[] Body);
