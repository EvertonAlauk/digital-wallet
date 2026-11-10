using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class AccountLifecycleApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    [Fact]
    public async Task Deactivate_Returns200WithTheBlockedStatus()
    {
        var api = new ApiClient(factory);
        var account = await CreateAsync(api, "to-deactivate");

        var response = await api.DeactivateAccountAsync(account.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Status.Should().Be("Blocked");
    }

    [Fact]
    public async Task Deposit_IntoAnInactiveAccount_IsRejected()
    {
        var api = new ApiClient(factory);
        var account = await CreateAsync(api, "frozen");
        await api.DeactivateAccountAsync(account.Id);

        var accepted = await api.PostDepositAsync(account.Id, 10m);
        await factory.DrainAsync();

        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var operationId = (await accepted.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!.OperationId;
        var operation = await (await api.GetOperationAsync(operationId)).Content.ReadFromJsonAsync<OperationResponse>();
        operation!.Status.Should().Be("Rejected");
    }

    [Fact]
    public async Task Activate_ReopensTheAccountForMovements()
    {
        var api = new ApiClient(factory);
        var account = await CreateAsync(api, "reopened");
        await api.DeactivateAccountAsync(account.Id);

        var response = await api.ActivateAccountAsync(account.Id);
        await api.FundAsync(account.Id, 25m);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Status.Should().Be("Active");
        (await (await api.GetAccountAsync(account.Id)).Content.ReadFromJsonAsync<AccountResponse>())!
            .Balance.Should().Be(25m);
    }

    [Fact]
    public async Task Deactivate_AnotherUsersAccount_Returns404AndChangesNothing()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var account = await CreateAsync(alice, "alice-only");

        var response = await bob.DeactivateAccountAsync(account.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await alice.GetAccountAsync(account.Id)).Content.ReadFromJsonAsync<AccountResponse>())!
            .Status.Should().Be("Active");
    }

    [Fact]
    public async Task Rename_Returns200WithTheNewName()
    {
        var api = new ApiClient(factory);
        var account = await CreateAsync(api, "old-name");

        var response = await api.RenameAccountAsync(account.Id, "  Holidays  ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Name.Should().Be("Holidays");
    }

    [Fact]
    public async Task Rename_ToBlank_ClearsTheName()
    {
        var api = new ApiClient(factory);
        var account = await CreateAsync(api, "to-clear");

        var response = await api.RenameAccountAsync(account.Id, "   ");

        (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Name.Should().BeNull();
    }

    [Fact]
    public async Task Rename_TooLong_Returns422()
    {
        var api = new ApiClient(factory);
        var account = await CreateAsync(api, "too-long");

        var response = await api.RenameAccountAsync(account.Id, new string('x', 61));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Rename_AnotherUsersAccount_Returns404()
    {
        var alice = new ApiClient(factory);
        var bob = new ApiClient(factory);
        var account = await CreateAsync(alice, "not-bobs");

        var response = await bob.RenameAccountAsync(account.Id, "Stolen");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListByStatus_ReturnsOnlyActiveOrOnlyInactiveAccounts()
    {
        var api = new ApiClient(factory);
        var active = await CreateAsync(api, "still-active");
        var inactive = await CreateAsync(api, "switched-off");
        await api.DeactivateAccountAsync(inactive.Id);

        var activeItems = (await (await api.ListAccountsAsync(status: "active")).Content.ReadFromJsonAsync<AccountListResponse>())!.Items;
        var inactiveItems = (await (await api.ListAccountsAsync(status: "inactive")).Content.ReadFromJsonAsync<AccountListResponse>())!.Items;

        activeItems.Select(account => account.Id).Should().Contain(active.Id).And.NotContain(inactive.Id);
        inactiveItems.Select(account => account.Id).Should().Contain(inactive.Id).And.NotContain(active.Id);
        activeItems.Should().OnlyContain(account => account.Status == "Active");
        inactiveItems.Should().OnlyContain(account => account.Status == "Blocked");
    }

    [Fact]
    public async Task ListByUnknownStatus_Returns400()
    {
        var response = await new ApiClient(factory).ListAccountsAsync(status: "sleeping");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Summary_SumsAllAccountsIncludingInactiveOnes()
    {
        var api = new ApiClient(factory);
        var first = await CreateAsync(api, "summary-one");
        var second = await CreateAsync(api, "summary-two");
        await api.FundAsync(first.Id, 100m);
        await api.FundAsync(second.Id, 50.5m);
        await api.DeactivateAccountAsync(second.Id);

        var summary = await (await api.GetAccountSummaryAsync()).Content.ReadFromJsonAsync<AccountSummaryResponse>();

        summary!.Totals.Should().ContainSingle(total => total.Currency == "BRL" && total.Amount == 150.5m);
        summary.ActiveCount.Should().Be(1);
        summary.InactiveCount.Should().Be(1);
    }

    [Fact]
    public async Task Summary_ForANewUser_HasNoTotals()
    {
        var response = await new ApiClient(factory).GetAccountSummaryAsync();

        var summary = await response.Content.ReadFromJsonAsync<AccountSummaryResponse>();
        summary!.Totals.Should().BeEmpty();
    }

    private static async Task<AccountResponse> CreateAsync(ApiClient api, string name) =>
        (await (await api.PostAccountAsync(name)).Content.ReadFromJsonAsync<AccountResponse>())!;
}

public class AccountPaginationApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    [Fact]
    public async Task ListPages_ReportTheTotalAndTheOffset()
    {
        var api = new ApiClient(factory);
        for (var index = 0; index < 3; index++)
        {
            await api.PostAccountAsync($"page-{index}");
        }

        var page = await (await api.ListAccountsAsync(limit: 2)).Content.ReadFromJsonAsync<AccountListResponse>();
        var next = await (await api.ListAccountsAsync(limit: 2, offset: 2)).Content.ReadFromJsonAsync<AccountListResponse>();

        page!.Items.Should().HaveCount(2);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(3);
        page.Offset.Should().Be(0);
        next!.Offset.Should().Be(2);
        next.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NegativeOffset_Returns400()
    {
        var response = await new ApiClient(factory).ListAccountsAsync(offset: -1);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
