using DigitalWallet.Application.Common;
using DigitalWallet.Application.Operations;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Domain.Events;
using DigitalWallet.Testing.Fakes;
using DigitalWallet.Testing.InMemory;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class OutboxTests
{
    [Fact]
    public async Task AcceptedTransfer_StagesTransferRequestedInTheSameCommit()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);
        var outboxBefore = harness.Database.OutboxMessages.Count;

        var accepted = await harness.AcceptTransferOnlyAsync(source, destination, 40m);

        var added = harness.Database.OutboxMessages.Skip(outboxBefore).ToList();
        added.Should().ContainSingle();
        var (message, publishedAt) = added[0];
        message.Type.Should().Be(nameof(TransferRequested));
        publishedAt.Should().BeNull();

        var payload = message.DeserializePayload<TransferRequested>();
        payload.OperationId.Should().Be(accepted.OperationId);
        payload.SourceAccountId.Should().Be(source);
        payload.DestinationAccountId.Should().Be(destination);
        payload.Amount.Should().Be(40m);
    }

    [Fact]
    public async Task RefusedAcceptance_LeavesNoEventInTheOutbox()
    {
        var harness = await WalletHarness.CreateAsync();
        var account = await harness.CreateFundedAccountAsync(100m);

        var before = harness.Database.OutboxMessages.Count;
        Func<Task> sameAccount = () => harness.AcceptTransferOnlyAsync(account, account, 10m);
        Func<Task> unknownAccount = () => harness.AcceptDepositOnlyAsync(Guid.NewGuid(), 10m);

        await sameAccount.Should().ThrowAsync<Exception>();
        await unknownAccount.Should().ThrowAsync<AccountNotFoundException>();
        harness.Database.OutboxMessages.Count.Should().Be(before, "refused requests leave no event");
    }

    [Fact]
    public async Task StagedEvent_IsInvisibleUntilTheUnitOfWorkCommits()
    {
        var database = new InMemoryDatabase();
        var message = OutboxMessage.Create("Test", new { Value = 1 }, DateTimeOffset.UnixEpoch);

        using (var unitOfWork = database.CreateUnitOfWork())
        {
            unitOfWork.Outbox.Enqueue(message);
            database.OutboxMessages.Should().BeEmpty("the event is staged, not committed");
        }

        // Disposed without commit: the staged event is gone, just like the change it describes.
        database.OutboxMessages.Should().BeEmpty();

        using (var unitOfWork = database.CreateUnitOfWork())
        {
            unitOfWork.Outbox.Enqueue(message);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        database.OutboxMessages.Should().ContainSingle().Which.Message.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task Relay_PublishesOnlyAfterCommit_AndMarksEachMessagePublished()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);
        await harness.AcceptTransferOnlyAsync(source, destination, 10m);

        var publisher = new InMemoryMessagePublisher();
        var relay = NewRelay(harness, publisher);
        var before = harness.Database.OutboxMessages.Count(m => m.Message.Type == nameof(TransferRequested));

        publisher.PublishCalls.Should().Be(0, "nothing is published until the relay runs");
        before.Should().Be(1);

        var published = await relay.PublishPendingAsync(100, CancellationToken.None);

        published.Should().Be(1);
        publisher.Delivered.Should().ContainSingle();
        harness.Database.OutboxMessages.Last().PublishedAt.Should().NotBeNull();
        (await relay.PublishPendingAsync(100, CancellationToken.None)).Should().Be(0, "a published message is not published again");
    }

    [Fact]
    public async Task BrokerFailure_KeepsTheMessagePendingUntilItSucceeds()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);
        await harness.AcceptTransferOnlyAsync(source, destination, 10m);

        var publisher = new InMemoryMessagePublisher { FailWhen = _ => true };
        var relay = NewRelay(harness, publisher);

        Func<Task> failing = () => relay.PublishPendingAsync(100, CancellationToken.None);
        await failing.Should().ThrowAsync<InvalidOperationException>();
        harness.Database.OutboxMessages.Last().PublishedAt.Should().BeNull();

        publisher.FailWhen = _ => false;
        (await relay.PublishPendingAsync(100, CancellationToken.None)).Should().Be(1);
        publisher.Delivered.Should().ContainSingle();
    }

    [Fact]
    public async Task NoEventIsLost_WhenTheBrokerFailsIntermittently()
    {
        // 40 accepted transfers, and the broker rejects every third publish. Running the relay until nothing is
        // pending must deliver every event exactly once.
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(1000m);
        var destination = await harness.CreateFundedAccountAsync(0m);
        for (var i = 0; i < 40; i++)
        {
            await harness.AcceptTransferOnlyAsync(source, destination, 1m);
        }

        var pendingAtStart = harness.Database.OutboxMessages
            .Where(m => m.PublishedAt is null)
            .Select(m => m.Message.Id)
            .ToList();

        var calls = 0;
        var publisher = new InMemoryMessagePublisher { FailWhen = _ => ++calls % 3 == 0 };
        var relay = NewRelay(harness, publisher);

        for (var attempt = 0; attempt < 500 && harness.Database.OutboxMessages.Any(m => m.PublishedAt is null); attempt++)
        {
            try
            {
                await relay.PublishPendingAsync(7, CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The broker was unavailable for this batch. The rest of the batch stays pending for the next run.
            }
        }

        var committedIds = pendingAtStart;
        var deliveredIds = publisher.Delivered.Select(m => m.Id).ToList();

        harness.Database.OutboxMessages.Where(m => committedIds.Contains(m.Message.Id)).Should().OnlyContain(m => m.PublishedAt != null);
        deliveredIds.Should().BeEquivalentTo(committedIds, "every committed event is delivered");
        deliveredIds.Should().OnlyHaveUniqueItems("failed publishes are not recorded, so none is delivered twice");
    }

    [Fact]
    public async Task Messages_AreDeliveredInCommitOrder()
    {
        var harness = await WalletHarness.CreateAsync();
        var source = await harness.CreateFundedAccountAsync(100m);
        var destination = await harness.CreateFundedAccountAsync(0m);
        for (var i = 0; i < 5; i++)
        {
            await harness.AcceptTransferOnlyAsync(source, destination, 1m);
        }

        var pendingAtStart = harness.Database.OutboxMessages
            .Where(m => m.PublishedAt is null)
            .Select(m => m.Message.Id)
            .ToList();

        var publisher = new InMemoryMessagePublisher();
        await NewRelay(harness, publisher).PublishPendingAsync(100, CancellationToken.None);

        publisher.Delivered.Select(m => m.Id).Should().Equal(pendingAtStart);
    }

    private static OutboxRelay NewRelay(WalletHarness harness, InMemoryMessagePublisher publisher) =>
        new(new InMemoryOutboxStore(harness.Database), publisher, harness.Clock);
}
