using DigitalWallet.Application.Ports;

namespace DigitalWallet.Testing.Fakes;

/// <summary>Clock whose time is controlled by the test.</summary>
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
