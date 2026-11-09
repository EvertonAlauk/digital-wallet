using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Statements;
using DigitalWallet.Api.Features.Transfers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.IntegrationTests.Infrastructure;

/// <summary>Typed helper over the test server, so tests read as business steps.</summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(WalletApiFactory factory)
    {
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

    public Task<HttpResponseMessage> PostDepositAsync(Guid accountId, decimal amount) =>
        _http.PostAsJsonAsync($"/accounts/{accountId}/deposits", new MovementRequest(amount));

    public Task<HttpResponseMessage> PostWithdrawalAsync(Guid accountId, decimal amount) =>
        _http.PostAsJsonAsync($"/accounts/{accountId}/withdrawals", new MovementRequest(amount));

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

    public Task<HttpResponseMessage> PostTransferAsync(Guid sourceAccountId, Guid destinationAccountId, decimal amount) =>
        _http.PostAsJsonAsync("/transfers", new TransferRequest(sourceAccountId, destinationAccountId, amount));

    public Task<HttpResponseMessage> PostRawJsonAsync(string path, string json) =>
        _http.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
}
