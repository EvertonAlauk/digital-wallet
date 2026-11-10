using DigitalWallet.Application.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalWallet.Api.Background;

/// <summary>
/// Periodically publishes pending outbox messages. A failure is logged and retried on the next tick; messages that
/// were not published stay pending, so nothing is lost while the broker is unavailable.
/// </summary>
internal sealed class OutboxRelayService(IServiceScopeFactory scopes, ILogger<OutboxRelayService> logger)
    : BackgroundService
{
    private const int BatchSize = 100;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // A scope per tick: the relay uses scoped services (database context, publisher, handlers).
                await using var scope = scopes.CreateAsyncScope();
                var relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();
                await relay.PublishPendingAsync(BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox relay failed. Pending messages will be retried.");
            }
        }
    }
}
