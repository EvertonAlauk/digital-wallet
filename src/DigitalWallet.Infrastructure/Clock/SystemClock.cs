using DigitalWallet.Application.Ports;

namespace DigitalWallet.Infrastructure.Clock;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
