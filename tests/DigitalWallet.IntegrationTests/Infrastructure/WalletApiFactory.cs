using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Ports;
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
/// Runs the real API pipeline (routing, validation, exception mapping, ProblemDetails) with the database
/// replaced by the in-memory fakes. Reusable by any integration test class.
/// </summary>
public sealed class WalletApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public InMemoryDatabase Database { get; } = new();

    public FakeClock Clock { get; } = new(StartTime);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" skips the Development-only migration step. Nothing connects to PostgreSQL.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", "Host=unused;Database=unused");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<WalletDbContext>();
            services.RemoveAll<IAccountRepository>();
            services.RemoveAll<ITransactionRepository>();
            services.RemoveAll<IUnitOfWork>();
            services.RemoveAll<ILedgerReader>();
            services.RemoveAll<IClock>();

            services.AddSingleton(Database);
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<ILedgerReader>(new InMemoryLedgerReader(Database));
            services.RemoveAll<IIdempotencyRepository>();
            services.AddSingleton<IIdempotencyRepository>(new InMemoryIdempotencyRepository(Database));

            // One unit of work per HTTP request, as with the EF Core DbContext.
            services.AddScoped<InMemoryUnitOfWork>(_ => Database.CreateUnitOfWork());
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<InMemoryUnitOfWork>());
            services.AddScoped<IAccountRepository>(provider => provider.GetRequiredService<InMemoryUnitOfWork>().Accounts);
            services.AddScoped<ITransactionRepository>(provider => provider.GetRequiredService<InMemoryUnitOfWork>().Transactions);
        });
    }
}
