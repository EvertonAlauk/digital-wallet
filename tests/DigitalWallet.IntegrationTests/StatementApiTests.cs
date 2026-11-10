using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Statements;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class StatementApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task Statement_PaginatesWithCursorUntilTheLastPage()
    {
        var account = await CreateAccountAsync();
        foreach (var amount in new[] { 10m, 20m, 30m })
        {
            await _api.FundAsync(account.Id, amount);
        }

        var first = await _api.GetStatementAsync(account.Id, limit: 2);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPage = (await first.Content.ReadFromJsonAsync<StatementResponse>())!;
        firstPage.Items.Select(item => item.Amount).Should().Equal(30m, 20m);
        firstPage.NextCursor.Should().NotBeNullOrEmpty();

        var second = await _api.GetStatementAsync(account.Id, firstPage.NextCursor, limit: 2);
        var secondPage = (await second.Content.ReadFromJsonAsync<StatementResponse>())!;
        secondPage.Items.Select(item => item.Amount).Should().Equal(10m);
        secondPage.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Statement_InvalidCursor_Returns400()
    {
        var account = await CreateAccountAsync();

        var response = await _api.GetStatementAsync(account.Id, cursor: "not a cursor!");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Statement_LimitAboveMaximum_Returns400()
    {
        var account = await CreateAccountAsync();

        var response = await _api.GetStatementAsync(account.Id, limit: 101);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Statement_UnknownAccount_Returns404()
    {
        var response = await _api.GetStatementAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<AccountResponse> CreateAccountAsync()
    {
        var response = await _api.PostAccountAsync();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }
}

public class StatementFilterApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    [Fact]
    public async Task FilterByType_ReturnsOnlyThatKindOverHttp()
    {
        var api = new ApiClient(factory);
        var account = (await (await api.PostAccountAsync("filter-type")).Content.ReadFromJsonAsync<AccountResponse>())!;
        await api.FundAsync(account.Id, 50m);
        await api.PostWithdrawalAsync(account.Id, 5m);
        await factory.DrainAsync();

        var response = await api.GetStatementAsync(account.Id, type: "Withdrawal");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<StatementResponse>();
        page!.Items.Should().NotBeEmpty().And.OnlyContain(item => item.Type == "Withdrawal");
    }

    [Fact]
    public async Task TypeFilter_IsCaseInsensitive()
    {
        var api = new ApiClient(factory);
        var account = (await (await api.PostAccountAsync("filter-case")).Content.ReadFromJsonAsync<AccountResponse>())!;
        await api.FundAsync(account.Id, 10m);

        var response = await api.GetStatementAsync(account.Id, type: "deposit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownType_Returns400()
    {
        var api = new ApiClient(factory);
        var account = (await (await api.PostAccountAsync("filter-bad-type")).Content.ReadFromJsonAsync<AccountResponse>())!;

        var response = await api.GetStatementAsync(account.Id, type: "Bonus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StartDateAfterEndDate_Returns400()
    {
        var api = new ApiClient(factory);
        var account = (await (await api.PostAccountAsync("filter-dates")).Content.ReadFromJsonAsync<AccountResponse>())!;

        var response = await api.GetStatementAsync(account.Id, from: "2026-02-01", to: "2026-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MalformedDate_Returns400()
    {
        var api = new ApiClient(factory);
        var account = (await (await api.PostAccountAsync("filter-bad-date")).Content.ReadFromJsonAsync<AccountResponse>())!;

        var response = await api.GetStatementAsync(account.Id, from: "01/01/2026");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DateFilter_ExcludesEntriesOutsideTheRange()
    {
        var api = new ApiClient(factory);
        var account = (await (await api.PostAccountAsync("filter-future")).Content.ReadFromJsonAsync<AccountResponse>())!;
        await api.FundAsync(account.Id, 20m);

        var response = await api.GetStatementAsync(account.Id, from: "2001-01-01", to: "2001-01-02");

        var page = await response.Content.ReadFromJsonAsync<StatementResponse>();
        page!.Items.Should().BeEmpty();
    }
}
