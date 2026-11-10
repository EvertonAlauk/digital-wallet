using DigitalWallet.Application.Notifications;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Domain.Events;
using DigitalWallet.Testing.Fakes;
using DigitalWallet.Testing.InMemory;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class NotificationConsumerTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    private static OutboxMessage TransferMessage() => OutboxMessage.Create(
        nameof(TransferCompleted),
        new TransferCompleted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15m, "BRL", DateTimeOffset.UnixEpoch),
        DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task DuplicateDelivery_SendsTheNotificationOnce()
    {
        var sender = new InMemoryNotificationSender();
        var handler = new TransferNotificationHandler(new InMemoryProcessedMessageStore(), sender);
        var message = TransferMessage();

        await handler.HandleAsync(message, CancellationToken.None);
        await handler.HandleAsync(message, CancellationToken.None);

        sender.Sent.Should().ContainSingle().Which.MessageId.Should().Be(message.Id);
    }

    [Fact]
    public async Task SendFailure_ReleasesTheMark_SoTheRetrySends()
    {
        var sender = new InMemoryNotificationSender { FailNext = 1 };
        var handler = new TransferNotificationHandler(new InMemoryProcessedMessageStore(), sender);
        var message = TransferMessage();

        Func<Task> first = () => handler.HandleAsync(message, CancellationToken.None);
        await first.Should().ThrowAsync<InvalidOperationException>();

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task BrokerRedeliversAfterLosingTheAck_NotificationIsSentOnce()
    {
        // The consumer handles the message, but the acknowledgement is lost, so the relay publishes it again.
        // Over 10 transfers, every message is delivered twice. Each notification must still be sent once.
        var harness = await WalletHarness.CreateAsync();

        // Ten committed TransferCompleted messages, as the transfer step writes them.
        using (var unitOfWork = harness.Database.CreateUnitOfWork())
        {
            for (var i = 0; i < 10; i++)
            {
                unitOfWork.Outbox.Enqueue(OutboxMessage.Create(
                    nameof(TransferCompleted),
                    new TransferCompleted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, "BRL", DateTimeOffset.UnixEpoch),
                    DateTimeOffset.UnixEpoch));
            }

            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        var sender = new InMemoryNotificationSender();
        var handler = new TransferNotificationHandler(new InMemoryProcessedMessageStore(), sender);
        var publisher = new AckLossPublisher(handler);
        var relay = new OutboxRelay(new InMemoryOutboxStore(harness.Database), publisher, harness.Clock);

        for (var attempt = 0; attempt < 100 && harness.Database.OutboxMessages.Any(m => m.PublishedAt is null); attempt++)
        {
            try
            {
                await relay.PublishPendingAsync(3, CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The acknowledgement was lost. The relay will publish the message again on its next run.
            }
        }

        publisher.DeliveryCount.Should().Be(20, "each message is delivered once, then once more after the lost ack");
        sender.Sent.Should().HaveCount(10);
        sender.Sent.Select(n => n.MessageId).Should().OnlyHaveUniqueItems();
    }

    // Hands the message to the handler, then fails the acknowledgement the first time for each message.
    private sealed class AckLossPublisher(IMessageHandler handler) : IMessagePublisher
    {
        private readonly HashSet<Guid> _acknowledgedOnce = new();

        public int DeliveryCount { get; private set; }

        public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            DeliveryCount++;
            await handler.HandleAsync(message, cancellationToken);

            if (_acknowledgedOnce.Add(message.Id))
            {
                throw new InvalidOperationException("The acknowledgement was lost.");
            }
        }
    }
}
