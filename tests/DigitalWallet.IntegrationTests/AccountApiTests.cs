using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Domain.Enums;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class AccountApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task CreateAccount_Returns201_OwnedByTheTokenSubject()
    {
        var response = await _api.PostAccountAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        account!.OwnerId.Should().Be(_api.UserId, "the owner is the caller, not a value sent in the body");
        account.Status.Should().Be(AccountStatus.Active.ToString());
        account.Balance.Should().Be(0m);
        response.Headers.Location.Should().Be(new Uri($"/accounts/{account.Id}", UriKind.Relative));
    }

    [Fact]
    public async Task CreateAccount_WithoutToken_Returns401()
    {
        var response = await ApiClient.Anonymous(factory).PostAccountAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
        var response = await _api.PostAccountAsync();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }
}

public class AccountNamesAndListingTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    [Fact]
    public async Task CreateAccount_WithAName_ReturnsTheName()
    {
        var api = new ApiClient(factory);

        var response = await api.PostAccountAsync("  Groceries  ");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Name.Should().Be("Groceries");
    }

    [Fact]
    public async Task CreateAccount_WithoutABody_HasNoName()
    {
        var response = await new ApiClient(factory).PostAccountAsync();

        (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Name.Should().BeNull();
    }

    [Fact]
    public async Task CreateAccount_NameTooLong_Returns422()
    {
        var response = await new ApiClient(factory).PostAccountAsync(new string('x', 61));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task CreateAccount_WithTheSameIdempotencyKey_CreatesOneAccount()
    {
        var api = new ApiClient(factory);
        var key = Guid.NewGuid().ToString();

        var first = await SendWithKeyAsync(api, key, "Once");
        var second = await SendWithKeyAsync(api, key, "Once");

        first.Id.Should().Be(second.Id);
        var listed = await (await api.ListAccountsAsync()).Content.ReadFromJsonAsync<AccountListResponse>();
        listed!.Items.Count(account => account.Name == "Once").Should().Be(1);
    }

    [Fact]
    public async Task ListAccounts_ReturnsOnlyTheCallersAccounts()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var aliceAccount = await CreateAsync(alice, "alice-list");
        await CreateAsync(bob, "bob-list");

        var response = await alice.ListAccountsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await response.Content.ReadFromJsonAsync<AccountListResponse>();
        listed!.Items.Should().OnlyContain(account => account.OwnerId == alice.UserId);
        listed.Items.Select(account => account.Id).Should().Contain(aliceAccount.Id);
    }

    [Fact]
    public async Task ListAccounts_NewUser_ReturnsAnEmptyList()
    {
        var response = await new ApiClient(factory).ListAccountsAsync();

        var listed = await response.Content.ReadFromJsonAsync<AccountListResponse>();
        listed!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAccounts_LimitOutOfRange_Returns400()
    {
        var response = await new ApiClient(factory).ListAccountsAsync(limit: 0);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListAccounts_WithoutToken_Returns401()
    {
        var response = await ApiClient.Anonymous(factory).ListAccountsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<AccountResponse> CreateAsync(ApiClient api, string name) =>
        (await (await api.PostAccountAsync(name)).Content.ReadFromJsonAsync<AccountResponse>())!;

    private static async Task<AccountResponse> SendWithKeyAsync(ApiClient api, string key, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/accounts")
        {
            Content = JsonContent.Create(new CreateAccountRequest(name)),
        };
        request.Headers.Add("Idempotency-Key", key);
        var response = await api.Http.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }
}
