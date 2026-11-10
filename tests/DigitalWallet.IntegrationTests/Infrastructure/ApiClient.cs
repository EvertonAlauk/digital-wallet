using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Transfers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Typed helper over the test server, so tests read as business steps. Each instance acts as one user: its requests
/// carry a bearer token whose subject is <see cref="UserId"/>.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly WalletApiFactory _factory;

    public ApiClient(WalletApiFactory factory)
        : this(factory, Guid.NewGuid(), roles: ["user"])
    {
    }

    public ApiClient(WalletApiFactory factory, Guid userId, IEnumerable<string> roles)
    {
        _factory = factory;
        UserId = userId;

        // HTTPS base address avoids the redirect from UseHttpsRedirection.
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokens.Create(userId, roles));
    }

    public Guid UserId { get; }

    public static ApiClient Admin(WalletApiFactory factory) =>
        new(factory, Guid.NewGuid(), roles: ["admin"]);

    /// <summary>A client that sends no token at all.</summary>
    public static ApiClient Anonymous(WalletApiFactory factory)
    {
        var client = new ApiClient(factory);
        client.Http.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    public HttpClient Http => _http;

    /// <summary>Creates an account owned by this client's user. The owner comes from the token, not from the request.</summary>
    public Task<HttpResponseMessage> PostAccountAsync() =>
        _http.PostAsync("/accounts", content: null);

    public Task<HttpResponseMessage> GetAccountAsync(Guid accountId) =>
        _http.GetAsync($"/accounts/{accountId}");

    /// <summary>Accepts a deposit (202). The balance changes only when the pipeline runs.</summary>
    public Task<HttpResponseMessage> PostDepositAsync(Guid accountId, decimal amount) =>
        _http.PostAsJsonAsync($"/accounts/{accountId}/deposits", new MovementRequest(amount));

    public Task<HttpResponseMessage> PostWithdrawalAsync(Guid accountId, decimal amount) =>
        _http.PostAsJsonAsync($"/accounts/{accountId}/withdrawals", new MovementRequest(amount));

    public Task<HttpResponseMessage> PostTransferAsync(Guid sourceAccountId, Guid destinationAccountId, decimal amount) =>
        _http.PostAsJsonAsync("/transfers", new TransferRequest(sourceAccountId, destinationAccountId, amount));

    public Task<HttpResponseMessage> GetOperationAsync(Guid operationId) =>
        _http.GetAsync($"/operations/{operationId}");

    /// <summary>Deposits and then runs the pipeline, so the balance is visible when this returns.</summary>
    public async Task FundAsync(Guid accountId, decimal amount)
    {
        using var response = await PostDepositAsync(accountId, amount);
        response.EnsureSuccessStatusCode();
        await _factory.DrainAsync();
    }

    public Task<HttpResponseMessage> GetStatementAsync(Guid accountId, string? cursor = null, int? limit = null)
    {
        var query = new List<string>();
        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (limit is not null)
        {
            query.Add($"limit={limit}");
        }

        var suffix = query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
        return _http.GetAsync($"/accounts/{accountId}/statement{suffix}");
    }

    public Task<HttpResponseMessage> PostRawJsonAsync(string path, string json) =>
        _http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
}
