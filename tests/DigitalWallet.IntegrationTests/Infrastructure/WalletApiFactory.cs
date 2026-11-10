using System.Globalization;
using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Application.Operations;
using DigitalWallet.Infrastructure.Persistence;
using DigitalWallet.Testing.Fakes;
using DigitalWallet.Testing.InMemory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DigitalWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API pipeline (routing, authentication, authorization, validation, exception mapping, ProblemDetails)
/// with PostgreSQL and RabbitMQ replaced by in-memory fakes. Tokens are signed locally (see <see cref="TestTokens"/>).
/// Background processing is driven explicitly through <see cref="DrainAsync"/>.
/// </summary>
public class WalletApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public InMemoryDatabase Database { get; } = new();

    public FakeClock Clock { get; } = new(StartTime);

    public InMemoryPipeline Pipeline { get; private set; } = null!;

    /// <summary>Requests per minute per user. Subclasses lower it to test the rate limit.</summary>
    public virtual int RateLimitPermitLimit => 100_000;

    /// <summary>Relays pending outbox messages through the handlers until nothing is left.</summary>
    public Task DrainAsync() => Pipeline.DrainAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" skips the Development-only migration step, and background services are not started.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", "Host=unused;Database=unused");
        builder.UseSetting("RabbitMq:Host", "unused");
        builder.UseSetting("RateLimit:PermitLimit", RateLimitPermitLimit.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            Pipeline = new InMemoryPipeline(Database, Clock);

            // Trust only the test key, issuer and audience. No metadata is fetched from a real identity provider.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
                options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                options.TokenValidationParameters.ValidAudience = TestTokens.Audience;
            });

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

/// <summary>A factory with a tiny rate limit, for the rate limit tests.</summary>
public sealed class LowRateLimitFactory : WalletApiFactory
{
    public override int RateLimitPermitLimit => 3;
}
