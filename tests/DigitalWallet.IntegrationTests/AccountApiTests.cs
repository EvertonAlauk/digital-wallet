using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Application.Ports;
using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class AccountApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task CreateAccount_Returns201WithLocationAndZeroBalance()
    {
        var ownerId = Guid.NewGuid();

        var response = await _api.PostAccountAsync(ownerId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        account!.OwnerId.Should().Be(ownerId);
        account.Status.Should().Be(AccountStatus.Active.ToString());
        account.Balance.Should().Be(0m);
        response.Headers.Location.Should().Be(new Uri($"/accounts/{account.Id}", UriKind.Relative));
    }

    [Fact]
    public async Task CreateAccount_EmptyOwner_Returns400ValidationProblem()
    {
        var response = await _api.PostAccountAsync(Guid.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task CreateAccount_MalformedJson_Returns400ProblemDetails()
    {
        var response = await _api.PostRawJsonAsync("/accounts", "{\"ownerId\":\"not-a-guid\"}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetAccount_Existing_Returns200WithBalance()
    {
        var created = await CreateAccountAsync();
        await _api.FundAsync(created.Id, 42m);

        var response = await _api.GetAccountAsync(created.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        account!.Balance.Should().Be(42m);
        account.Currency.Should().Be("BRL");
    }

    [Fact]
    public async Task GetAccount_Unknown_Returns404ProblemDetails()
    {
        var response = await _api.GetAccountAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<AccountResponse> CreateAccountAsync()
    {
        var response = await _api.PostAccountAsync(Guid.NewGuid());
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }
}
