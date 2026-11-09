namespace DigitalWallet.Application.Ports;

/// <summary>Source of the current time. Replaced by a fake in tests so time is deterministic.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
