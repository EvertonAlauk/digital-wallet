using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Domain.Enums;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class MovementApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task Deposit_Returns202WithTheOperationLocation()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostDepositAsync(account, 150.5m);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = (await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!;
        accepted.OperationId.Should().NotBeEmpty();
        accepted.Status.Should().Be("Accepted");
        response.Headers.Location.Should().Be(new Uri($"/operations/{accepted.OperationId}", UriKind.Relative));
    }

    [Fact]
    public async Task Deposit_CompletesInTheBackground_AndCreditsTheBalance()
    {
        var account = await CreateAccountAsync();

        var accepted = await AcceptedAsync(await _api.PostDepositAsync(account, 150.5m));
        await factory.DrainAsync();

        var operation = await OperationAsync(accepted.OperationId);
        operation.Status.Should().Be(OperationStatus.Completed.ToString());
        operation.TransactionId.Should().NotBeNull();
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(150.5m);
    }

    [Fact]
    public async Task Deposit_RecordsDoubleEntryInTheLedger()
    {
        var account = await CreateAccountAsync();

        var accepted = await AcceptedAsync(await _api.PostDepositAsync(account, 20m));
        await factory.DrainAsync();

        var transactionId = (await OperationAsync(accepted.OperationId)).TransactionId!.Value;
        var transaction = factory.Database.GetTransaction(transactionId);
        transaction.Should().NotBeNull();
        transaction!.Entries.Should().HaveCount(2);
        transaction.Entries.Sum(entry => entry.Amount.Amount).Should().Be(0m);
        factory.Database.GetAccount(SettlementAccount.Id)!.Balance.Amount.Should().BeLessThan(0m);
    }

    [Fact]
    public async Task Withdrawal_CompletesInTheBackground()
    {
        var account = await CreateAccountAsync();
        await _api.FundAsync(account, 100m);

        var accepted = await AcceptedAsync(await _api.PostWithdrawalAsync(account, 30m));
        await factory.DrainAsync();

        (await OperationAsync(accepted.OperationId)).Status.Should().Be(OperationStatus.Completed.ToString());
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(70m);
    }

    [Fact]
    public async Task Withdrawal_InsufficientFunds_IsRejectedAndKeepsTheBalance()
    {
        var account = await CreateAccountAsync();
        await _api.FundAsync(account, 10m);

        var accepted = await AcceptedAsync(await _api.PostWithdrawalAsync(account, 11m));
        await factory.DrainAsync();

        var operation = await OperationAsync(accepted.OperationId);
        operation.Status.Should().Be(OperationStatus.Rejected.ToString());
        operation.FailureReason.Should().Be("Insufficient funds.");
        factory.Database.GetAccount(account)!.Balance.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task Deposit_ToBlockedAccount_IsRejectedInTheBackground()
    {
        var account = await CreateAccountAsync();
        await BlockAccountAsync(account);

        var accepted = await AcceptedAsync(await _api.PostDepositAsync(account, 5m));
        await factory.DrainAsync();

        var operation = await OperationAsync(accepted.OperationId);
        operation.Status.Should().Be(OperationStatus.Rejected.ToString());
        operation.FailureReason.Should().Be("The account is blocked.");
    }

    [Fact]
    public async Task Deposit_ZeroAmount_Returns422Immediately()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostDepositAsync(account, 0m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Deposit_MoreThanTwoDecimals_Returns400WithFieldErrors()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostDepositAsync(account, 1.001m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("errors").TryGetProperty("amount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Deposit_UnknownAccount_Returns404Immediately()
    {
        var response = await _api.PostDepositAsync(Guid.NewGuid(), 10m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOperation_Unknown_Returns404()
    {
        var response = await _api.GetOperationAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task<Guid> CreateAccountAsync()
    {
        var response = await _api.PostAccountAsync(Guid.NewGuid());
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
    }

    private static async Task<OperationAcceptedResponse> AcceptedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!;
    }

    private async Task<OperationResponse> OperationAsync(Guid operationId)
    {
        var response = await _api.GetOperationAsync(operationId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
    }

    private async Task BlockAccountAsync(Guid accountId)
    {
        using var unitOfWork = factory.Database.CreateUnitOfWork();
        var account = await unitOfWork.Accounts.GetByIdAsync(accountId, CancellationToken.None);
        account!.Block();
        await unitOfWork.CommitAsync(CancellationToken.None);
    }
}
