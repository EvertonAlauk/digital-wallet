using System.Threading.RateLimiting;
using DigitalWallet.Api.Auth;
using DigitalWallet.Api.Background;
using DigitalWallet.Api.Common;
using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Admin;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.Api.Features.Statements;
using DigitalWallet.Api.Features.Transfers;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Infrastructure;
using DigitalWallet.Infrastructure.Messaging.RabbitMq;
using DigitalWallet.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddRabbitMq(new RabbitMqOptions(
    Host: builder.Configuration["RabbitMq:Host"] ?? "localhost",
    Port: int.TryParse(builder.Configuration["RabbitMq:Port"], out var port) ? port : 5672,
    UserName: builder.Configuration["RabbitMq:Username"] ?? "guest",
    Password: builder.Configuration["RabbitMq:Password"] ?? "guest"));

// Background processing (outbox relay and RabbitMQ consumers) is not started in tests, which drive it explicitly.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<OutboxRelayService>();
    builder.Services.AddHostedService<RabbitMqConsumerService>();
}

builder.Services.AddDigitalWalletAuthentication(builder.Configuration, builder.Environment);

// Requests per minute for each user (per client address when anonymous). Secures the API against abuse and runaway clients.
var permitLimit = builder.Configuration.GetValue("RateLimit:PermitLimit", 100);
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirst("sub")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddApplicationUseCases();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi(options => options.AddBearerAuthentication());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development only: bring the database schema up to date on startup.
    await using var migrationScope = app.Services.CreateAsyncScope();
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<WalletDbContext>();
    await dbContext.Database.MigrateAsync();
}

// The settlement account is the counterpart of every deposit and withdrawal. Creating it is idempotent.
await using (var startupScope = app.Services.CreateAsyncScope())
{
    await startupScope.ServiceProvider
        .GetRequiredService<EnsureSettlementAccountUseCase>()
        .ExecuteAsync(CancellationToken.None);
}

// API documentation is available in every environment except Production.
if (!app.Environment.IsProduction())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DigitalWallet v1"));
}

app.UseExceptionHandler();
// Error responses that the framework sends without a body (e.g. 401 or 403) get a ProblemDetails body.
app.UseStatusCodePages();
app.UseHttpsRedirection();

// Authentication runs before the rate limiter, so the limit is per user, and before the idempotency middleware,
// so unauthenticated requests never reach it.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAccountEndpoints();
app.MapMovementEndpoints();
app.MapStatementEndpoints();
app.MapTransferEndpoints();
app.MapOperationEndpoints();
app.MapAdminEndpoints();

app.Run();

// Makes the entry point visible to the integration tests (WebApplicationFactory<Program>).
public partial class Program;
