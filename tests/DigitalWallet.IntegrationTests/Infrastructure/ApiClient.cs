using System.Net.Http.Json;
using System.Text;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Transfers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.IntegrationTests.Infrastructure;

/// <summary>Typed helper over the test server, so tests read as business steps.</summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly WalletApiFactory _factory;

    public ApiClient(WalletApiFactory factory)
    {
        _factory = factory;

        // HTTPS base address avoids the redirect from UseHttpsRedirection.
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
    }

    public Task<HttpResponseMessage> PostAccountAsync(Guid ownerId) =>
        _http.PostAsJsonAsync("/accounts", new CreateAccountRequest(ownerId));

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

    public HttpClient Http => _http;
}
