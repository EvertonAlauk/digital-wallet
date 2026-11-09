using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Api.Features.Transfers;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class TransferApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task Transfer_Returns201WithBothBalances()
    {
        var source = await CreateFundedAccountAsync(200m);
        var destination = await CreateFundedAccountAsync(0m);

        var response = await _api.PostTransferAsync(source, destination, 75m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var transfer = (await response.Content.ReadFromJsonAsync<TransferResponse>())!;
        transfer.SourceBalance.Should().Be(125m);
        transfer.DestinationBalance.Should().Be(75m);
        transfer.TransactionId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Transfer_ToSameAccount_Returns422()
    {
        var account = await CreateFundedAccountAsync(100m);

        var response = await _api.PostTransferAsync(account, account, 10m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_Returns422AndChangesNothing()
    {
        var source = await CreateFundedAccountAsync(20m);
        var destination = await CreateFundedAccountAsync(0m);

        var response = await _api.PostTransferAsync(source, destination, 21m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
        factory.Database.GetAccount(source)!.Balance.Amount.Should().Be(20m);
        factory.Database.GetAccount(destination)!.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task Transfer_UnknownAccount_Returns404()
    {
        var source = await CreateFundedAccountAsync(20m);

        var response = await _api.PostTransferAsync(source, Guid.NewGuid(), 1m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> CreateFundedAccountAsync(decimal balance)
    {
        var created = (await (await _api.PostAccountAsync(Guid.NewGuid())).Content.ReadFromJsonAsync<AccountResponse>())!;
        if (balance > 0m)
        {
            await _api.PostDepositAsync(created.Id, balance);
        }

        return created.Id;
    }
}
