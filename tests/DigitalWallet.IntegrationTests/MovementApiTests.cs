using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Application.Ports;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class MovementApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task Deposit_Returns201AndUpdatesBalance()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostDepositAsync(account.Id, 150.5m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var movement = await response.Content.ReadFromJsonAsync<MovementResponse>();
        movement!.Type.Should().Be("Deposit");
        movement.AccountBalance.Should().Be(150.5m);
        movement.TransactionId.Should().NotBeEmpty();
        response.Headers.Location.Should().Be(new Uri($"/accounts/{account.Id}", UriKind.Relative));
    }

    [Fact]
    public async Task Deposit_RecordsDoubleEntryInTheLedger()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostDepositAsync(account.Id, 20m);

        var movement = (await response.Content.ReadFromJsonAsync<MovementResponse>())!;
        var transaction = factory.Database.GetTransaction(movement.TransactionId);
        transaction.Should().NotBeNull();
        transaction!.Entries.Should().HaveCount(2);
        transaction.Entries.Sum(entry => entry.Amount.Amount).Should().Be(0m);
        factory.Database.GetAccount(SettlementAccount.Id)!.Balance.Amount.Should().BeLessThan(0m);
    }

    [Fact]
    public async Task Withdrawal_Returns201AndUpdatesBalance()
    {
        var account = await CreateAccountAsync();
        await _api.PostDepositAsync(account.Id, 100m);

        var response = await _api.PostWithdrawalAsync(account.Id, 30m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var movement = await response.Content.ReadFromJsonAsync<MovementResponse>();
        movement!.Type.Should().Be("Withdrawal");
        movement.AccountBalance.Should().Be(70m);
    }

    [Fact]
    public async Task Withdrawal_InsufficientFunds_Returns422AndKeepsBalance()
    {
        var account = await CreateAccountAsync();
        await _api.PostDepositAsync(account.Id, 10m);

        var response = await _api.PostWithdrawalAsync(account.Id, 11m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        factory.Database.GetAccount(account.Id)!.Balance.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task Deposit_ZeroAmount_Returns422()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostDepositAsync(account.Id, 0m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Deposit_MoreThanTwoDecimals_Returns400WithFieldErrors()
    {
        var account = await CreateAccountAsync();

        var response = await _api.PostDepositAsync(account.Id, 1.001m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("errors").TryGetProperty("amount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Deposit_UnknownAccount_Returns404()
    {
        var response = await _api.PostDepositAsync(Guid.NewGuid(), 10m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deposit_BlockedAccount_Returns422()
    {
        var account = await CreateAccountAsync();
        await BlockAccountAsync(account.Id);

        var response = await _api.PostDepositAsync(account.Id, 5m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    private async Task<AccountResponse> CreateAccountAsync()
    {
        var response = await _api.PostAccountAsync(Guid.NewGuid());
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private async Task BlockAccountAsync(Guid accountId)
    {
        var unitOfWork = factory.Database.CreateUnitOfWork();
        var account = await unitOfWork.Accounts.GetByIdAsync(accountId, CancellationToken.None);
        account!.Block();
        await unitOfWork.CommitAsync(CancellationToken.None);
    }
}
