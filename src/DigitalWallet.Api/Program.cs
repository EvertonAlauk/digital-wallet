using DigitalWallet.Api.Common;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Statements;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Infrastructure;
using DigitalWallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplicationUseCases();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

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
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DigitalWallet v1"));
}

app.UseExceptionHandler();
// Error responses that the framework sends without a body (e.g. malformed request bodies) get a ProblemDetails body.
app.UseStatusCodePages();
app.UseHttpsRedirection();

app.MapHealthChecks("/health");
app.MapAccountEndpoints();
app.MapMovementEndpoints();
app.MapStatementEndpoints();

app.Run();

// Makes the entry point visible to the integration tests (WebApplicationFactory<Program>).
public partial class Program;
