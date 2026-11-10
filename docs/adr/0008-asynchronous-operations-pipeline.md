# ADR 0008: Asynchronous operations pipeline with retry, dead-letter queue and dead-letter topic

## Context

Deposits, withdrawals and transfers were processed inside the HTTP request. That makes the request slow, and it couples the client's latency to the database. The team wants the request to be accepted quickly, with the money movement done in the background through RabbitMQ.

## Decisions

### 1. Three steps per operation, and only one of them moves money

| Step | Message | What it does | Changes balances? |
|---|---|---|---|
| Accept | (HTTP request) | Validates the input and records the operation as `Accepted`, with the requested-step message in the outbox. Returns `202` with the operation id. | No |
| Requested | `DepositRequested`, `WithdrawalRequested`, `TransferRequested` | Advisory checks (account exists, not blocked, enough funds). Requests the created step, or records the rejection on the operation. | No |
| Created | `DepositCreated`, `WithdrawalCreated`, `TransferCreated` | Locks the accounts, validates again, applies the ledger entries, and completes the operation. | **Yes** |

The outcome of every operation is recorded on the operation itself (`Completed` or `Rejected`, with the reason). The client reads it with `GET /operations/{id}`. No message is published for the outcome, because nothing outside the wallet consumes one.

### 2. The requested checks are advisory; the created step is authoritative

Between the requested and created steps, a balance can change. Two withdrawals can both pass the requested check for the same money. The created step locks the accounts, validates every entry before changing any balance, and rejects the loser. A test covers this race: one withdrawal completes and the other is rejected, and the balance never goes negative.

### 3. Business rule failures are rejections, not retries

A rule violation (insufficient funds, blocked account) is a terminal outcome. The step writes `Rejected` on the operation, in the same commit, and the message is acknowledged. Only technical failures (database down, broker down, unexpected exceptions) are retried.

### 4. Every step is idempotent by state

A step does nothing unless the operation is still `Accepted`. The created step locks the accounts before it reads the operation, so a duplicate delivery that runs at the same time waits, then sees the committed result. A test delivers the same created message twice at once and checks that the money moves once.

### 5. Queues: main, retry (`-dlq`) and dead-letter topic (`-dlt`)

For each message type `X`, named in kebab case (`deposit-created`):

- `x`: the main queue, consumed by the handler.
- `x-dlq`: the retry queue. Messages wait 5 seconds (`x-message-ttl`), then return to `x` through the exchange.
- `x-dlt`: the dead-letter topic. Messages that failed every retry stay here.

The exchange is `wallet` (direct). Six message types produce 18 queues.

The attempt number is in the `x-attempt` header. The first delivery has attempt 0. A failure with attempt `n` goes to the retry queue with `n + 1` while `n < 5`, and to the dead-letter topic after that. So a message is delivered at most six times: the original and five retries.

Replaying a message from the dead-letter topic: copy it in the management UI and publish it to the main queue. **Set `x-attempt` back to 0 when you copy it.** Otherwise the first failure sends it straight back to the dead-letter topic.

### 6. Publisher confirms

The outbox relay publishes with publisher confirms. A publish returns only after the broker accepts the message, so the outbox row is marked published only after the broker has it. The retry and dead-letter publishes use the same channel.

### 7. The API returns 202 with a link to the outcome

`POST /accounts/{id}/deposits`, `/withdrawals` and `/transfers` return `202 Accepted` with the operation id and a
`Location` header pointing to `GET /operations/{id}`. Synchronous errors (unknown account, invalid amount, same-account transfer) still return their status at once, because the request can be refused without the pipeline.

## Verification

- **Unit (in-memory pipeline, real handlers).** Acceptance writes nothing to balances. Requested checks reject blocked accounts and insufficient funds. Withdrawals racing for one balance: one completes. Duplicate requested and created messages do not move money twice, including concurrent duplicates. Operation state transitions are enforced. The retry rule allows five retries and then dead-letters.
- **Integration (HTTP, in-memory pipeline).** Accepted requests return 202 and complete after the pipeline runs. Rejections are visible through `GET /operations/{id}`. Idempotency keys still replay the 202. 1,000 concurrent transfers among five accounts keep the three invariants (no negative customer balance, the total is unchanged, each cached balance equals its ledger).
- **RabbitMQ (manual, Docker Compose).** All 18 queues are declared. A deposit completed in about two seconds. A transfer moved the money, and a withdrawal above the balance was rejected with the reason. The outbox had no pending messages. A malformed message on `deposit-requested` made five retries into `deposit-requested-dlq`, then went to `deposit-requested-dlt` with `x-attempt: 5`.

## Consequences

- The client's request does not wait for the database or the broker, so the response time no longer depends on the money movement.
- An operation can be seen as `Accepted` for a short time. Clients must poll `GET /operations/{id}`.
- Retried messages can be delayed by up to about 30 seconds (five retries, five seconds each) before they reach the dead-letter topic.
- The per-account locks of ADR 0005 now run in the created step, so concurrent operations on one account are serialized there.

## Open questions

- **Consumer acknowledgement after a crash.** A consumer that stops after its database commit but before it acks receives the message again. The step is idempotent by state, so this is safe, but it is worth a test with a real broker.
- **Requested messages do not lock.** The requested step reads balances without locks. This is intentional, because the created step validates again. A flood of requests on one account moves the rejections to the created step.
- **Dead-letter topic has no consumer.** Failed messages wait for a person. An alert on the depth of the `-dlt` queues would help.
- **Operations are never purged.** The `operations` table keeps every request.

## Change log

- **2026-11-10:** removed the `TransferCompleted` event, the `*Rejected` events and the notification consumers. Nothing outside the wallet consumes them. Rejections are recorded on the operation, and the queues drop from 30 to 18.
