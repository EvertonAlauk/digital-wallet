using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalWallet.IntegrationTests;

public class SecurityTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    // Routes that are public on purpose: the health check and the API documentation.
    private static readonly string[] PublicRoutes =
    [
        "/health",
        "/openapi/{documentName}.json",
    ];

    [Fact]
    public async Task EveryRoute_RequiresAuthentication_ExceptThePublicOnes()
    {
        var anonymous = ApiClient.Anonymous(factory);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var checkedRoutes = 0;
        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText ?? string.Empty;
            if (PublicRoutes.Contains(route))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var method in methods)
            {
                // Replace every route parameter with a real-looking id. Authentication runs before binding, so the
                // value does not matter: the request must be refused with 401 before reaching the endpoint.
                var path = Regex.Replace(route, @"\{[^}]+\}", Guid.NewGuid().ToString());
                var request = new HttpRequestMessage(new HttpMethod(method), "/" + path.TrimStart('/'));

                var response = await anonymous.Http.SendAsync(request);

                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {route} must require authentication");
                checkedRoutes++;
            }
        }

        // Guard against the test passing because it found nothing to check.
        checkedRoutes.Should().BeGreaterThanOrEqualTo(8, "accounts, movements, transfers, statements, operations and admin all have routes");
    }

    [Fact]
    public async Task Health_IsPublic()
    {
        var response = await ApiClient.Anonymous(factory).Http.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_IsPublic()
    {
        var response = await ApiClient.Anonymous(factory).Http.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresBearerAuthentication_OnEveryProtectedOperation()
    {
        var document = await ApiClient.Anonymous(factory).Http.GetFromJsonAsync<JsonNode>("/openapi/v1.json");

        var scheme = document!["components"]!["securitySchemes"]!["Bearer"]!;
        scheme["type"]!.GetValue<string>().Should().Be("http");
        scheme["scheme"]!.GetValue<string>().Should().Be("bearer");

        var operations = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation => (Route: path.Key, Method: operation.Key, Node: operation.Value!)))
            .ToList();
        operations.Should().NotBeEmpty();

        foreach (var operation in operations)
        {
            var requirements = operation.Node["security"]?.AsArray()
                .Select(requirement => requirement?.AsObject().Select(scheme => scheme.Key).ToList())
                .ToList();
            requirements.Should().NotBeNull($"{operation.Method} {operation.Route} must show that it needs a token");
            requirements!.Should().Contain(keys => keys!.Contains("Bearer"), $"{operation.Method} {operation.Route} must name the Bearer scheme");
        }

        var adminOperations = operations.Where(operation => operation.Route.StartsWith("/api/admin/", StringComparison.Ordinal));
        adminOperations.Should().NotBeEmpty();
        foreach (var operation in adminOperations)
        {
            operation.Node["responses"]!["403"].Should().NotBeNull($"{operation.Method} {operation.Route} is restricted to admins");
        }
    }

    [Fact]
    public async Task AdminRoute_WithoutTheAdminRole_Returns403()
    {
        var customer = new ApiClient(factory);
        var account = await CreateAccountAsync(customer);

        var response = await customer.Http.GetAsync($"/api/admin/accounts/{account}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminRoute_WithoutAToken_Returns401()
    {
        var response = await ApiClient.Anonymous(factory).Http.GetAsync($"/api/admin/accounts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminRoute_WithTheAdminRole_ReadsAnyAccount()
    {
        var customer = new ApiClient(factory);
        var account = await CreateAccountAsync(customer);

        var response = await ApiClient.Admin(factory).Http.GetAsync($"/api/admin/accounts/{account}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AccountResponse>())!.OwnerId.Should().Be(customer.UserId);
    }

    [Fact]
    public async Task Admin_CannotMoveMoneyOfAnotherCustomer()
    {
        // The admin role reads accounts for support. It is not a way to act on someone else's money.
        var customer = new ApiClient(factory);
        var account = await CreateAccountAsync(customer);

        var response = await ApiClient.Admin(factory).PostDepositAsync(account, 10m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TokenWithAnotherAudience_Returns401()
    {
        var token = TestTokens.Create(Guid.NewGuid(), roles: ["user"], audience: "some-other-api");

        var response = await WithToken(token).GetAccountAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenFromAnotherIssuer_Returns401()
    {
        var token = TestTokens.Create(Guid.NewGuid(), roles: ["user"], issuer: "https://evil.example/realms/x");

        var response = await WithToken(token).GetAccountAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var token = TestTokens.Create(
            Guid.NewGuid(),
            roles: ["user"],
            expires: DateTimeOffset.UtcNow.AddMinutes(-10));

        var response = await WithToken(token).GetAccountAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenSignedByAnUntrustedKey_Returns401()
    {
        var token = TestTokens.Create(Guid.NewGuid(), roles: ["admin"], signingKey: TestTokens.UntrustedKey);

        var response = await WithToken(token).Http.GetAsync($"/api/admin/accounts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a forged admin token must not be accepted");
    }

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersAccount()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var account = await CreateAccountAsync(alice);

        var response = await bob.GetAccountAsync(account);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Customer_CannotDepositIntoAnotherCustomersAccount()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var account = await CreateAccountAsync(alice);

        var response = await bob.PostDepositAsync(account, 10m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersStatement()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var account = await CreateAccountAsync(alice);

        var response = await bob.GetStatementAsync(account);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_CannotTransferFromAnotherCustomersAccount()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var aliceAccount = await CreateAccountAsync(alice);
        var bobAccount = await CreateAccountAsync(bob);
        await alice.FundAsync(aliceAccount, 50m);

        var response = await bob.PostTransferAsync(aliceAccount, bobAccount, 10m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Database.GetAccount(aliceAccount)!.Balance.Amount.Should().Be(50m);
    }

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersOperation()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var account = await CreateAccountAsync(alice);
        var accepted = (await (await alice.PostDepositAsync(account, 5m)).Content.ReadFromJsonAsync<OperationAcceptedResponse>())!;

        var byBob = await bob.GetOperationAsync(accepted.OperationId);
        var byAlice = await alice.GetOperationAsync(accepted.OperationId);

        byBob.StatusCode.Should().Be(HttpStatusCode.NotFound);
        byAlice.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Customer_CanSendMoneyToAnotherCustomer()
    {
        // Only the source must belong to the caller. The destination is anyone's account.
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var aliceAccount = await CreateAccountAsync(alice);
        var bobAccount = await CreateAccountAsync(bob);
        await alice.FundAsync(aliceAccount, 50m);

        var response = await alice.PostTransferAsync(aliceAccount, bobAccount, 10m);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    private ApiClient WithToken(string token)
    {
        var client = new ApiClient(factory);
        client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CreateAccountAsync(ApiClient client)
    {
        var response = await client.PostAccountAsync();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
    }
}
