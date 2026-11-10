using System.Threading.RateLimiting;
using DigitalWallet.Bff.Auth;
using DigitalWallet.Bff.Identity;
using DigitalWallet.Bff.Keycloak;
using DigitalWallet.Bff.Proxy;
using DigitalWallet.Bff.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<KeycloakOptions>()
    .Bind(builder.Configuration.GetSection(KeycloakOptions.SectionName));

// Fails at startup when a setting is missing, instead of failing on the first login.
var keycloakOptions = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>() ?? new();
keycloakOptions.Validate();
StaticUi.Validate(builder.Configuration);

// The timeout bounds every call to Keycloak, so a slow identity provider cannot hold a request open forever.
builder.Services.AddHttpClient<IIdentityService, KeycloakIdentityService>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<IUserRegistrationService, KeycloakUserRegistrationService>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddBffSessions();

// The API that /api/* is forwarded to. Required: a BFF without its API address would fail every proxied request.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
if (!DigitalWallet.Bff.Common.HttpUrl.IsAbsoluteHttp(apiBaseUrl))
{
    throw new InvalidOperationException("Api:BaseUrl must be an absolute http or https URL, for example http://localhost:8080.");
}

builder.Services.AddReverseProxy().LoadFromMemory(ApiProxy.Routes(), ApiProxy.Clusters(apiBaseUrl));

var generalLimit = builder.Configuration.GetValue("RateLimit:GeneralPermitLimit", 60);
var loginLimit = builder.Configuration.GetValue("RateLimit:LoginPermitLimit", 5);
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = async (context, cancellationToken) =>
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests", detail: "Wait a moment and try again.")
            .ExecuteAsync(context.HttpContext);

    limiter.AddPolicy(RateLimitPolicies.General, context => ClientLimiter(context, generalLimit));
    limiter.AddPolicy(RateLimitPolicies.Login, context => ClientLimiter(context, loginLimit));
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseStatusCodePages();
app.UseRateLimiter();
StaticUi.UseFrontend(app);
app.UseLoginSessionRotation();
app.UseAuthentication();
app.UseAuthorization();

// The API documentation exists only in Development, so it is not published in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DigitalWallet BFF v1"));
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapBffEndpoints();
app.MapApiProxy();
StaticUi.MapFrontendFallback(app);

app.Run();

static RateLimitPartition<string> ClientLimiter(HttpContext context, int permitLimit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

// Makes the entry point visible to the tests (WebApplicationFactory<Program>).
public partial class Program;
