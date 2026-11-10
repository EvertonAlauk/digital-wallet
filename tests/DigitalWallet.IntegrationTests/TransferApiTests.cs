using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.IntegrationTests.Infrastructure;
using DigitalWallet.Domain.Enums;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class TransferApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task Transfer_Returns202AndCompletesInTheBackground()
    {
        var source = await CreateAccountAsync();
        var destination = await CreateAccountAsync();
        await _api.FundAsync(source, 200m);

        var response = await _api.PostTransferAsync(source, destination, 75m);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = (await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!;

        await factory.DrainAsync();

        (await OperationAsync(accepted.OperationId)).Status.Should().Be(OperationStatus.Completed.ToString());
        factory.Database.GetAccount(source)!.Balance.Amount.Should().Be(125m);
        factory.Database.GetAccount(destination)!.Balance.Amount.Should().Be(75m);
    }

    [Fact]
    public async Task Transfer_ToSameAccount_Returns422Immediately()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostTransferAsync(account, account, 10m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_IsRejectedAndChangesNothing()
    {
        var source = await CreateAccountAsync();
        var destination = await CreateAccountAsync();
        await _api.FundAsync(source, 20m);

        var response = await _api.PostTransferAsync(source, destination, 21m);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = (await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!;

        await factory.DrainAsync();

        var operation = await OperationAsync(accepted.OperationId);
        operation.Status.Should().Be(OperationStatus.Rejected.ToString());
        factory.Database.GetAccount(source)!.Balance.Amount.Should().Be(20m);
        factory.Database.GetAccount(destination)!.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task Transfer_UnknownAccount_Returns404Immediately()
    {
        var source = await CreateAccountAsync();

        var response = await _api.PostTransferAsync(source, Guid.NewGuid(), 1m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> CreateAccountAsync()
    {
        var response = await _api.PostAccountAsync(Guid.NewGuid());
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
    }

    private async Task<OperationResponse> OperationAsync(Guid operationId)
    {
        var response = await _api.GetOperationAsync(operationId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
    }
}
