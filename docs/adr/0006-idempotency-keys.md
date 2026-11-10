# ADR 0006: Idempotency-Key for money movements

- **Status:** Accepted
- **Date:** 2026-11-10

## Context

A client that retries a deposit, withdrawal or transfer after a timeout cannot tell whether the first request was processed. Without protection, the retry moves money a second time. Each movement must be processed at most once per key, and a retry must receive the original outcome.

## Decisions

### 1. The key is persisted in PostgreSQL, unique on the key itself

Table `idempotency_keys`. The primary key is the key string, so two concurrent requests cannot both create it.
Insertion uses `INSERT ... ON CONFLICT ("Key") DO NOTHING`. The statement affects one row for the request that creates the key, and zero rows for everyone else, in one atomic step.

### 2. A request-scoped decision, in the application layer

`IdempotencyService.AcquireAsync` decides, in this order:

| State of the key | Result |
|---|---|
| No key, or expired (`ExpiresAt <= now`) | `Acquired`: process the request |
| Same payload, completed | `Replay`: return the stored response |
| Different payload (any state) | `PayloadMismatch`: 422 |
| Same payload, still processing | `InProgress`: 409 |

Expired entries are removed by a conditional delete (`DELETE ... WHERE "Key" = @k AND "ExpiresAt" <= @now`).
A fresh entry written by another request is never removed.

### 3. A reusable middleware, enabled per endpoint

`IdempotencyMiddleware` handles POST requests to endpoints marked with `.WithIdempotency()`. The deposit, withdrawal and transfer endpoints are marked. The middleware:

- reads the raw body and hashes it together with the method, path and query, so the same key cannot be reused for another request;
- captures the response, stores it when the status is 2xx, and sends it to the client;
- releases the key on any other outcome, so a retry is processed again.

The `Idempotency-Key` header is optional. Requests without it behave as before. Keys are limited to 255 characters.

### 4. Only successful responses are stored

Failed requests (validation, business rules, not found, server errors, conflicts) release their key. They move no money, so processing a retry again is safe, and it avoids replaying a stale error (for example, a 422 caused by a balance that has since been topped up).

### 5. Retention: 24 hours

`ExpiresAt = StartedAt + 24h`. An expired key is treated as absent. Expiry is checked when a key is read, and the expired row is then deleted. Rows that are never read again are not purged yet (see open questions).

## Verification

- **Unit (in-memory).** Replay returns the stored response; a different payload is a mismatch; a key in progress is reported as such; a released key can be used again; a key is expired exactly at 24 hours and not one second earlier; 50 concurrent acquisitions of the same key yield exactly one `Acquired`.
- **Integration (in-memory, through HTTP).** Same key and payload returns the original response and creates no second movement; a different payload returns 422; 20 concurrent requests with one key process one deposit; an expired key is processed again; a transfer with a repeated key is not moved twice; a failed request releases its key; without the header, every request is processed; an oversized key returns 400.
- **PostgreSQL (manual, Docker Compose).** Repeated request: replayed with `Idempotent-Replayed: true`, same body, one movement. Different payload: 422. 30 concurrent requests with one key: 28 × 201 (one processed, the rest replayed or in progress) and 2 × 409, one new transaction, balance correct. Expiry forced in the database: the same key is processed again.

## Consequences

- Retries are safe for clients that send the header.
- The database holds one row per key for 24 hours (the rows are not purged yet).
- Each deposit, withdrawal or transfer with a key makes two commits: one to claim the key and one to complete it, plus the movement's own commit.

## Known limitation: a crash between commits

The movement and the key's completion are separate commits. If the process stops after the movement commits and before the key is completed, the key stays `IsCompleted = false`. A retry then receives 409 "outcome unknown" until the key expires, which is 24 hours.

This is deliberate: the alternative (taking over a stale key) could process the same movement twice while the original request is still running. A 409 is safe, though it blocks that key for up to 24 hours. Callers can use a new key to make the operation again.

The clean fix is to store the completion in the same database transaction as the movement. That needs the movement's unit of work to write the idempotency row, which the current design does not do.

## Open questions

- **Purging.** Expired rows are deleted only when their key is read again. A periodic job should delete `ExpiresAt <= now()` in batches.
- **Same-transaction completion.** See the known limitation above. The movement and the key's completion should be one commit.
- **Stale in-progress keys.** A key stuck in progress is never released before expiry. An operator tool to release it would help.
- **Scope.** Keys are global, not per client, because there is no authentication yet. Once there is, keys should be scoped to the client.
