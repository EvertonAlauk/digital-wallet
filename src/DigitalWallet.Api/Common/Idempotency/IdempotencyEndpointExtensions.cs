namespace DigitalWallet.Api.Common.Idempotency;

/// <summary>Marks an endpoint whose POST requests honor the Idempotency-Key header.</summary>
public sealed class IdempotentEndpointMetadata;

public static class IdempotencyEndpointExtensions
{
    /// <summary>Makes the endpoint honor the Idempotency-Key header. Requests without the header are unaffected.</summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new IdempotentEndpointMetadata());
}
