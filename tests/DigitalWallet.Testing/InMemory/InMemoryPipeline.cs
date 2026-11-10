using DigitalWallet.Application.Operations;
using DigitalWallet.Application.Operations.Handlers;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;

namespace DigitalWallet.Testing.InMemory;

/// <summary>
/// Runs the real message handlers without a broker: a published message is dispatched to the handler of its type,
/// which runs in its own unit of work. <see cref="DrainAsync"/> keeps relaying the outbox until nothing is pending,
/// which is what the background services do over time.
/// </summary>
public sealed class InMemoryPipeline
{
    private readonly InMemoryDatabase _database;
    private readonly IClock _clock;
    private readonly Dictionary<string, Func<InMemoryUnitOfWork, IMessageHandler>> _handlers;

    public InMemoryPipeline(InMemoryDatabase database, IClock clock)
    {
        _database = database;
        _clock = clock;

        _handlers = new Dictionary<string, Func<InMemoryUnitOfWork, IMessageHandler>>
        {
            [nameof(DepositRequested)] = uow => new DepositRequestedHandler(uow.Accounts, uow.Operations, uow.Outbox, uow, clock),
            [nameof(DepositCreated)] = uow => new DepositCreatedHandler(uow.Accounts, uow.Transactions, uow.Operations, uow, clock),
            [nameof(WithdrawalRequested)] = uow => new WithdrawalRequestedHandler(uow.Accounts, uow.Operations, uow.Outbox, uow, clock),
            [nameof(WithdrawalCreated)] = uow => new WithdrawalCreatedHandler(uow.Accounts, uow.Transactions, uow.Operations, uow, clock),
            [nameof(TransferRequested)] = uow => new TransferRequestedHandler(uow.Accounts, uow.Operations, uow.Outbox, uow, clock),
            [nameof(TransferCreated)] = uow => new TransferCreatedHandler(uow.Accounts, uow.Transactions, uow.Operations, uow, clock),
        };

        Publisher = new DispatchingPublisher(this);
    }

    /// <summary>The publisher that the outbox relay uses in this pipeline.</summary>
    public IMessagePublisher Publisher { get; }

    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(message.Type, out var create))
        {
            throw new InvalidOperationException($"No handler is registered for message type {message.Type}.");
        }

        using var unitOfWork = _database.CreateUnitOfWork();
        await create(unitOfWork).HandleAsync(message, cancellationToken);
    }

    /// <summary>Relays pending messages until none is left. Handlers may produce new messages, so this loops.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        var relay = new OutboxRelay(new InMemoryOutboxStore(_database), Publisher, _clock);

        for (var round = 0; round < 1000; round++)
        {
            if (await relay.PublishPendingAsync(100, cancellationToken) == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("The pipeline did not settle: messages keep being produced.");
    }

    private sealed class DispatchingPublisher(InMemoryPipeline pipeline) : IMessagePublisher
    {
        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken) =>
            pipeline.DispatchAsync(message, cancellationToken);
    }
}
