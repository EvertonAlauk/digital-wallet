using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Application.Outbox;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

public class OutboxApiTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly ApiClient _api = new(factory);

    [Fact]
    public async Task AcceptedTransfer_StagesTransferRequestedInTheOutbox()
    {
        var source = await CreateAccountAsync();
        var destination = await CreateAccountAsync();
        await _api.FundAsync(source, 100m);
        var before = factory.Database.OutboxMessages.Count;

        var response = await _api.PostTransferAsync(source, destination, 25m);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var added = factory.Database.OutboxMessages.Skip(before).ToList();
        added.Should().ContainSingle();
        added[0].Message.Type.Should().Be(nameof(DigitalWallet.Application.Operations.TransferRequested));
        added[0].PublishedAt.Should().BeNull("the relay has not run yet");
    }

    [Fact]
    public async Task RefusedTransfer_LeavesNothingInTheOutbox()
    {
        var account = await CreateAccountAsync();
        var before = factory.Database.OutboxMessages.Count;

        var response = await _api.PostTransferAsync(account, account, 10m);

        response.StatusCode.Should().Be((HttpStatusCode)422);
        factory.Database.OutboxMessages.Count.Should().Be(before);
    }

    [Fact]
    public async Task CompletedTransfer_PublishesEveryPipelineStep()
    {
        var source = await CreateAccountAsync();
        var destination = await CreateAccountAsync();
        await _api.FundAsync(source, 100m);

        await _api.PostTransferAsync(source, destination, 10m);
        await factory.DrainAsync();

        var types = factory.Database.OutboxMessages.Select(m => m.Message.Type).ToList();
        types.Should().Contain(nameof(DigitalWallet.Application.Operations.TransferCreated));
        factory.Database.OutboxMessages.Should().OnlyContain(m => m.PublishedAt != null);
    }

    private async Task<Guid> CreateAccountAsync()
    {
        var response = await _api.PostAccountAsync();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
    }
}
