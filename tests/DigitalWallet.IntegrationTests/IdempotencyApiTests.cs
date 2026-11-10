using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Transfers;
using DigitalWallet.IntegrationTests.Infrastructure;
using DigitalWallet.Application.Idempotency;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.IntegrationTests;

public class IdempotencyApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task SameKeyAndPayload_ReturnsTheOriginalResponseWithoutProcessingAgain()
    {
        var account = await CreateAccountAsync();
        var key = Guid.NewGuid().ToString();
        var transactionsBefore = factory.Database.TransactionCount;

        var first = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(25m), key);
        var second = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(25m), key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeTrue();

        var firstBody = await first.Content.ReadFromJsonAsync<MovementResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<MovementResponse>();
        secondBody!.TransactionId.Should().Be(firstBody!.TransactionId);
        second.Headers.Location.Should().Be(first.Headers.Location);

        factory.Database.TransactionCount.Should().Be(transactionsBefore + 1, "the second request must not create a movement");
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(25m);
    }

    [Fact]
    public async Task SameKeyWithDifferentPayload_Returns422AndChangesNothing()
    {
        var account = await CreateAccountAsync();
        var key = Guid.NewGuid().ToString();

        await PostAsync($"/accounts/{account}/deposits", new MovementRequest(10m), key);
        var conflicting = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(11m), key);

        conflicting.StatusCode.Should().Be((HttpStatusCode)422);
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task ConcurrentRequestsWithTheSameKey_ProcessOnlyOnce()
    {
        var account = await CreateAccountAsync();
        var key = Guid.NewGuid().ToString();
        var transactionsBefore = factory.Database.TransactionCount;

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            PostAsync($"/accounts/{account}/deposits", new MovementRequest(7m), key)))
            .WaitAsync(TimeSpan.FromSeconds(60));

        try
        {
            // Each request either processed the deposit (201), replayed it (201), or was told it is still in progress (409).
            responses.Select(response => response.StatusCode)
                .Should().OnlyContain(status => status == HttpStatusCode.Created || status == HttpStatusCode.Conflict);

            var created = responses.Where(response => response.StatusCode == HttpStatusCode.Created).ToList();
            created.Should().NotBeEmpty();

            var transactionIds = new HashSet<Guid>();
            foreach (var response in created)
            {
                transactionIds.Add((await response.Content.ReadFromJsonAsync<MovementResponse>())!.TransactionId);
            }

            transactionIds.Should().HaveCount(1, "every successful response must refer to the same movement");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        factory.Database.TransactionCount.Should().Be(transactionsBefore + 1, "exactly one deposit must be processed");
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(7m);
    }

    [Fact]
    public async Task ExpiredKey_IsProcessedAgain()
    {
        var account = await CreateAccountAsync();
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(5m), key);
        factory.Clock.Advance(TimeSpan.FromHours(25));
        var second = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(5m), key);

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeFalse();
        var firstBody = await first.Content.ReadFromJsonAsync<MovementResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<MovementResponse>();
        secondBody!.TransactionId.Should().NotBe(firstBody!.TransactionId);
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task TransferWithTheSameKey_IsNotMovedTwice()
    {
        var source = await CreateAccountAsync();
        var destination = await CreateAccountAsync();
        await _api.PostDepositAsync(source, 100m);
        var key = Guid.NewGuid().ToString();
        var request = new TransferRequest(source, destination, 30m);

        var first = await PostAsync("/transfers", request, key);
        var second = await PostAsync("/transfers", request, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeTrue();
        factory.Database.GetAccount(source)!.Balance.Amount.Should().Be(70m);
        factory.Database.GetAccount(destination)!.Balance.Amount.Should().Be(30m);
    }

    [Fact]
    public async Task FailedRequest_ReleasesTheKey_SoARetryIsProcessed()
    {
        var account = await CreateAccountAsync();
        var key = Guid.NewGuid().ToString();

        var rejected = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(0m), key);
        var retry = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(4m), key);

        rejected.StatusCode.Should().Be((HttpStatusCode)422);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeFalse();
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(4m);
    }

    [Fact]
    public async Task WithoutTheHeader_EveryRequestIsProcessed()
    {
        var account = await CreateAccountAsync();

        var first = (await _api.PostDepositAsync(account, 3m)).Headers.Location;
        var second = await _api.PostDepositAsync(account, 3m);

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeFalse();
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(6m);
        first.Should().NotBeNull();
    }

    [Fact]
    public async Task KeyLongerThanTheLimit_Returns400()
    {
        var account = await CreateAccountAsync();
        var tooLong = new string('k', IdempotencyService.MaxKeyLength + 1);

        var response = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(1m), tooLong);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<Guid> CreateAccountAsync()
    {
        var response = await _api.PostAccountAsync(Guid.NewGuid());
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
    }

    private Task<HttpResponseMessage> PostAsync<T>(string path, T body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(IdempotencyMiddleware.HeaderName, key);
        return _http.SendAsync(request);
    }
}
