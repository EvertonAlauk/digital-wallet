using System.Text.Json;

namespace DigitalWallet.Application.Outbox;

/// <summary>An event waiting to be published. Written in the same commit as the change that produced it.</summary>
public sealed record OutboxMessage(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt)
{
    public static OutboxMessage Create<TPayload>(string type, TPayload payload, DateTimeOffset occurredAt) =>
        new(Guid.NewGuid(), type, JsonSerializer.Serialize(payload), occurredAt);

    public TPayload DeserializePayload<TPayload>() =>
        JsonSerializer.Deserialize<TPayload>(Payload)
        ?? throw new InvalidOperationException($"Message {Id} of type {Type} has an empty payload.");
}
