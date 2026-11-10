using System.Threading.RateLimiting;
using DigitalWallet.Bff.Auth;
using DigitalWallet.Bff.Keycloak;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<KeycloakOptions>()
    .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName));

// Fails at startup when a setting is missing, instead of failing on the first login.
var keycloakOptions = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>() ?? new();
keycloakOptions.Validate();

builder.Services.AddHttpClient<KeycloakClient>();

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(AuthRateLimits.PolicyName, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthRateLimits.PermitLimit,
                Window = AuthRateLimits.Window,
                QueueLimit = 0,
            }));
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseStatusCodePages();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();

// The API documentation exists only in Development, so it is not published in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DigitalWallet BFF v1"));
}

app.Run();

// Makes the entry point visible to the tests (WebApplicationFactory<Program>).
public partial class Program;
