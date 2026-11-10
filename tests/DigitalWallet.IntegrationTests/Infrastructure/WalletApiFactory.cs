using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Application.Operations;
using DigitalWallet.Infrastructure.Persistence;
using DigitalWallet.Testing.Fakes;
using DigitalWallet.Testing.InMemory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API pipeline (routing, validation, exception mapping, ProblemDetails) with PostgreSQL and RabbitMQ
/// replaced by in-memory fakes. Background processing is driven explicitly through <see cref="DrainAsync"/>.
/// </summary>
public sealed class WalletApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public InMemoryDatabase Database { get; } = new();

    public FakeClock Clock { get; } = new(StartTime);

    public InMemoryPipeline Pipeline { get; private set; } = null!;

    /// <summary>Relays pending outbox messages through the handlers until nothing is left.</summary>
    public Task DrainAsync() => Pipeline.DrainAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" skips the Development-only migration step, and background services are not started.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", "Host=unused;Database=unused");
        builder.UseSetting("RabbitMq:Host", "unused");

        builder.ConfigureTestServices(services =>
        {
            Pipeline = new InMemoryPipeline(Database, Clock);

            services.RemoveAll<WalletDbContext>();
            services.RemoveAll<IAccountRepository>();
            services.RemoveAll<ITransactionRepository>();
            services.RemoveAll<IOperationRepository>();
            services.RemoveAll<IUnitOfWork>();
            services.RemoveAll<IOutbox>();
            services.RemoveAll<IOutboxStore>();
            services.RemoveAll<IMessagePublisher>();
            services.RemoveAll<ILedgerReader>();
            services.RemoveAll<IClock>();
            services.RemoveAll<IIdempotencyRepository>();

            services.AddSingleton(Database);
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<ILedgerReader>(new InMemoryLedgerReader(Database));
            services.AddSingleton<IIdempotencyRepository>(new InMemoryIdempotencyRepository(Database));
            services.AddSingleton<IMessagePublisher>(Pipeline.Publisher);

            // One unit of work per HTTP request, as with the EF Core DbContext. Disposing it releases its locks.
            services.AddScoped<InMemoryUnitOfWork>(_ => Database.CreateUnitOfWork());
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<InMemoryUnitOfWork>());
            services.AddScoped<IAccountRepository>(provider => provider.GetRequiredService<InMemoryUnitOfWork>().Accounts);
            services.AddScoped<ITransactionRepository>(provider => provider.GetRequiredService<InMemoryUnitOfWork>().Transactions);
            services.AddScoped<IOperationRepository>(provider => provider.GetRequiredService<InMemoryUnitOfWork>().Operations);
            services.AddScoped<IOutbox>(provider => provider.GetRequiredService<InMemoryUnitOfWork>().Outbox);
            services.AddScoped<IOutboxStore>(_ => new InMemoryOutboxStore(Database));
        });
    }
}
