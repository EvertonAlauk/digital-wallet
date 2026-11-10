# ADR 0007: Transactional outbox for the operations pipeline

## Context

The pipeline moves a request through two steps: `Requested` (checks) and `Created` (moves the money). Between them a message must be handed off reliably. Saving the request and publishing the next step to a broker are two different systems, so there is no transaction that covers both.

Two naive orders both fail:

- **Publish, then commit.** If the commit fails after the message is published, the next step runs for an operation that was never recorded.
- **Commit, then publish.** If the process stops or the broker is down after the commit, the operation exists and the next step never runs. The operation stays `Accepted` forever, and nothing in the database shows that anything is missing.

A retry does not fix either case, because the code cannot tell whether the publish already happened.

## Decision

Use the transactional outbox pattern for every handoff between pipeline steps.

1. **Same commit.** The message is written as a row in `outbox_messages`, through the same `DbContext` and the same `SaveChanges` as the operation change. `IOutbox.Enqueue` stages the message in the unit of work, and the commit writes it or nothing. A refused request never reaches the enqueue, so it leaves no message.
2. **Relay after commit.** `OutboxRelay` reads pending messages in `Sequence` order, publishes each one through `IMessagePublisher`, and marks it published only after the publish succeeds. A failure stops the batch; the failed message and the rest stay pending for the next run.
3. **At-least-once delivery.** A crash between "publish" and "mark published" publishes the message again. Consumers must therefore be idempotent, and the pipeline steps are idempotent by the operation's state (ADR 0008).
4. **Background relay.** `OutboxRelayService` runs the relay every second in the API. It is not started in the `Testing` environment, where tests call the relay directly.

### Broker adapter

`IMessagePublisher` is implemented by `RabbitMqPublisher`. It publishes with publisher confirms, so a message is marked published only after the broker has accepted it. The in-process publisher used before RabbitMQ was removed.

## Verification

- **Unit (fakes, no broker).**
  - A committed request stages its next-step message in the same commit.
  - A refused request, and a transfer to the same account, leave no message.
  - A staged message is invisible until commit, and a disposed unit of work drops it.
  - Nothing is published before the relay runs; each published message is marked once.
  - When the broker fails, the message stays pending and is published once the broker recovers.
  - With the broker failing every third publish, 40 messages all reach the broker exactly once, with no duplicates.
  - Messages are delivered in commit order.
- **Integration (HTTP, in-memory).** An accepted transfer adds one unpublished message. A refused transfer adds none. After the pipeline drains, every message is published.
- **PostgreSQL (manual, Docker Compose).** A refused transfer (422) left no row. An accepted request produced a row in `outbox_messages`, which the relay marked published. The pending count was 0.

## Consequences

- A message exists if and only if the change that produced it committed.
- Delivery may repeat, so every step must be idempotent. The pipeline steps are, by the operation's state.
- The next step runs a few seconds after the request, which is expected for an asynchronous pipeline.
- The outbox table grows. Published rows are kept for now (see open questions).

## Problem this solves (short version)

A database commit and a message publish cannot be made atomic together. The outbox turns the publish into a row written with the change, so the database is the only system that has to be atomic. A separate process then moves the row to the broker, and retries until it succeeds.

## Open questions

- **Several relay instances.** Two API instances would read the same pending rows and publish duplicates. Duplicates are safe (steps are idempotent), but the relay should claim rows with `FOR UPDATE SKIP LOCKED` to avoid the waste.
- **Purging.** Published messages are never deleted. A job should remove them after a retention period.
- **Broker acknowledgements.** The consumer acknowledges only after its handler succeeds (ADR 0008). Behaviour after a crash between the commit and the acknowledgement should be checked with a real broker.
- **Ordering across messages.** Ordering is guaranteed within one relay run only. Steps that depend on order must rely on the operation's state.
