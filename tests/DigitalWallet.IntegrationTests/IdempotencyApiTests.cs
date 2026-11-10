using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Common.Idempotency;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.Api.Features.Transfers;
using DigitalWallet.Application.Idempotency;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class IdempotencyApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task SameKeyAndPayload_ReturnsTheOriginalAcceptanceWithoutProcessingAgain()
    {
        var account = await CreateAccountAsync();
        var key = Guid.NewGuid().ToString();
        var transactionsBefore = factory.Database.TransactionCount;

        var first = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(25m), key);
        var second = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(25m), key);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeTrue();

        var firstBody = await first.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        secondBody!.OperationId.Should().Be(firstBody!.OperationId);

        await factory.DrainAsync();

        factory.Database.TransactionCount.Should().Be(transactionsBefore + 1, "the second request must not create an operation");
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
        await factory.DrainAsync();
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
            // Each request either was accepted (202), replayed the acceptance (202), or was told it is still in progress (409).
            responses.Select(response => response.StatusCode)
                .Should().OnlyContain(status => status == HttpStatusCode.Accepted || status == HttpStatusCode.Conflict);

            var operationIds = new HashSet<Guid>();
            foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Accepted))
            {
                operationIds.Add((await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!.OperationId);
            }

            operationIds.Should().HaveCount(1, "every accepted response must refer to the same operation");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await factory.DrainAsync();
        factory.Database.TransactionCount.Should().Be(transactionsBefore + 1, "exactly one deposit must be processed");
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(7m);
    }

    [Fact]
    public async Task ExpiredKey_IsAcceptedAgain()
    {
        var account = await CreateAccountAsync();
        var key = Guid.NewGuid().ToString();

        var first = (await (await PostAsync($"/accounts/{account}/deposits", new MovementRequest(5m), key))
            .Content.ReadFromJsonAsync<OperationAcceptedResponse>())!;
        factory.Clock.Advance(TimeSpan.FromHours(25));

        var second = await PostAsync($"/accounts/{account}/deposits", new MovementRequest(5m), key);

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeFalse();
        (await second.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!.OperationId.Should().NotBe(first.OperationId);

        await factory.DrainAsync();
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task TransferWithTheSameKey_IsNotAcceptedTwice()
    {
        var source = await CreateAccountAsync();
        var destination = await CreateAccountAsync();
        await _api.FundAsync(source, 100m);
        var key = Guid.NewGuid().ToString();
        var request = new TransferRequest(source, destination, 30m);

        var first = await PostAsync("/transfers", request, key);
        var second = await PostAsync("/transfers", request, key);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeTrue();

        await factory.DrainAsync();
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
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);
        retry.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeFalse();

        await factory.DrainAsync();
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(4m);
    }

    [Fact]
    public async Task WithoutTheHeader_EveryRequestIsProcessed()
    {
        var account = await CreateAccountAsync();

        await _api.PostDepositAsync(account, 3m);
        var second = await _api.PostDepositAsync(account, 3m);

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).Should().BeFalse();

        await factory.DrainAsync();
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(6m);
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
        var response = await _api.PostAccountAsync();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
    }

    private Task<HttpResponseMessage> PostAsync<T>(string path, T body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(IdempotencyMiddleware.HeaderName, key);
        return _api.Http.SendAsync(request);
    }
}
