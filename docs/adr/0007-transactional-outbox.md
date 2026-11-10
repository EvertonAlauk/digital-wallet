# ADR 0007: Transactional outbox for domain events

## Context

When a transfer completes, other systems must be told about it (notifications, analytics, fraud checks). Saving the transfer and publishing the event to a broker are two different systems, so there is no transaction that covers both.

Two naive orders both fail:

- **Publish, then commit.** If the commit fails after the event is published, other systems act on a transfer that does not exist.
- **Commit, then publish.** If the process stops or the broker is down after the commit, the transfer exists and nobody is told. The event is lost, and nothing in the database shows that anything is missing.

A retry does not fix either case, because the code cannot tell whether the publish already happened.

## Decision

Use the transactional outbox pattern.

1. **Same commit.** The event is written as a row in `outbox_messages`, through the same `DbContext` and the same `SaveChanges` as the ledger and balance changes. `IOutbox.Enqueue` stages the message in the unit of work; the commit writes it or nothing. A rejected transfer never reaches the enqueue, so it leaves no event.
2. **Relay after commit.** `OutboxRelay` reads pending messages in `Sequence` order, publishes each one through `IMessagePublisher`, and marks it published only after the publish succeeds. A failure stops the batch; the failed message and the rest stay pending for the next run.
3. **At-least-once delivery.** A crash between "publish" and "mark published" publishes the message again. Consumers must therefore be idempotent.
4. **Idempotent consumers.** `TransferNotificationHandler` records each message id in `processed_messages` before sending (`INSERT ... ON CONFLICT DO NOTHING`). A redelivered message is ignored. If sending fails, the mark is removed, so the retry sends it.
5. **Background relay.** `OutboxRelayService` runs the relay every second in the API. It is not started in the `Testing` environment, where tests call the relay directly.

### Broker adapter

`IMessagePublisher` is implemented today by `InProcessMessagePublisher`, which delivers to the handlers of the same process. It keeps the whole flow working without a broker. The RabbitMQ publisher will replace it. That needs the `RabbitMQ.Client` package, which is waiting for approval. The broker itself already runs in `docker-compose.yml`.

## Verification

- **Unit (fakes, no broker).**
  - A committed transfer stages exactly one `TransferCompleted` in the same commit.
  - A rejected transfer, and a transfer to the same account, leave no event.
  - A staged event is invisible until commit, and a disposed unit of work drops it.
  - Nothing is published before the relay runs; each published message is marked once.
  - When the broker fails, the message stays pending and is published once the broker recovers.
  - With the broker failing every third publish, 40 transfers all reach the broker exactly once, with no duplicates in the delivered list.
  - Messages are delivered in commit order.
  - A consumer that loses the acknowledgement receives the message twice, and the notification is sent once.
  - A notification that fails to send is retried and sent once.
- **Integration (HTTP, in-memory).** A transfer adds one outbox message, unpublished. A rejected transfer adds none.
- **PostgreSQL (manual, Docker Compose).** A rejected transfer (422) left no row. A committed transfer produced one row in `outbox_messages`, which the relay marked published. The pending count was 0, `processed_messages` had one row, and the consumer logged the notification.

## Consequences

- An event exists if and only if the change that produced it committed.
- Delivery may repeat, so every consumer needs an idempotency check (the `processed_messages` table is the pattern).
- Events for one transfer are published after the transfer is visible, which can be a few seconds late.
- The outbox table grows. Published rows are kept for now (see open questions).

## Problem this solves (short version)

A database commit and a message publish cannot be made atomic together. The outbox turns the publish into a row written with the change, so the database is the only system that has to be atomic. A separate process then moves the row to the broker, and retries until it succeeds.

## Open questions

- **Account blocking event.** `AccountBlocked` is not raised yet, because there is no block use case. It should be enqueued by that use case, in the same way as `TransferCompleted`.
- **Several relay instances.** Two API instances would read the same pending rows and publish duplicates. Duplicates are safe (consumers are idempotent), but the relay should claim rows with `FOR UPDATE SKIP LOCKED` to avoid the waste.
- **Purging.** Published messages are never deleted. A job should remove them after a retention period.
- **Broker acknowledgements.** With RabbitMQ, the consumer must acknowledge only after the handler succeeds, and the publisher must wait for the broker's confirm before marking the row published.
- **Ordering across events.** Ordering is guaranteed within one relay run only. Consumers that depend on order must use the event `OccurredAt` or a per-account sequence.
