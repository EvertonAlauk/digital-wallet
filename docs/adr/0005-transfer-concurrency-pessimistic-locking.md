# ADR 0005: Transfer concurrency control: pessimistic row locks with ordered acquisition

## Context

A transfer debits one account and credits another in a single transaction. Two transfers that touch the same account at the same time can each read the same balance and both succeed, overdrawing the account (a lost update).
The wallet must never produce a negative customer balance, must never lose money, and must never deadlock when transfers run in opposite directions (A→B and B→A).

## Options considered

### A. Optimistic concurrency with retry

Read both accounts without locks. Commit with the `Version` check (`UPDATE ... WHERE Version = <read>`). On a conflict, reload and retry.

- Good: no locks held while reading. Scales well when conflicts are rare.
- Bad: with many transfers over a few accounts, most attempts conflict and retry. Under this kind of contention the work is repeated many times, and a bounded retry count can still fail requests.
- Bad: a retry needs a fresh unit of work and a fresh load. With EF Core the tracked entities of the failed attempt must be discarded, which makes the retry loop easy to get wrong.

### B. Pessimistic locking with `SELECT ... FOR UPDATE` (chosen)

Open a database transaction. Lock both account rows with `SELECT ... FOR UPDATE`, in ascending id order. Read the balances after the lock. Apply the entries. Insert the transaction and the ledger entries. Commit, which releases the locks.

- Good: no retries. Each transfer waits its turn, and it sees the latest committed balance because it reads after locking.
- Good: the lock duration is one short transaction, so the cost is bounded and predictable.
- Bad: transfers on the same account are serialized. A "hot" account limits throughput to one transfer at a time.
- Bad: a waiting transfer holds its request open. No lock timeout is configured yet (see open questions).

### C. Serializable isolation level

Let PostgreSQL detect the anomaly and abort one of the transactions, then retry.

- Equivalent to A in practice: the retry problem is the same, and the abort rate is unpredictable. Rejected.

## Decision

Transfers use option B.

1. **Lock order is ascending account id.** `GetByIdsForUpdateAsync` sorts the ids before the query, and `ORDER BY "Id" FOR UPDATE` makes PostgreSQL lock the rows in that order. Every transfer locks its accounts in the same global order, so a cycle of waiting transactions (A waits for B while B waits for A) cannot form.
2. **Balances are read after the lock.** `TransferUseCase` reads the accounts only through the locking query, so it never works from a stale balance.
3. **One database transaction.** `IUnitOfWork.BeginTransactionAsync` opens it before the lock, and `CommitAsync` commits the transfer, the ledger entries and the balance changes together. A failure in any business rule rolls everything back.
4. **Same-account transfers are rejected before locking.** Locking the same row twice would wait on itself.
5. **The version check is kept.** Deposits and withdrawals keep using only the version check. This is safe next to the locks: a deposit that read an account before a transfer committed fails its `WHERE Version = ...` check and gets a 409. A transfer that waits for a deposit gets the deposit's committed balance after the lock.

## Verification

- **Unit (fakes).** `InMemoryUnitOfWork` takes one lock per account and holds it until commit or dispose, acquiring locks in ascending id order. Tests cover: opposite-order lock requests complete; a second lock waits for the holder; locks are released on commit and on dispose; a missing account releases the locks already taken; ten concurrent transfers from one account never overdraw; A→B and B→A running together complete, with a timeout guard.
- **Integration (in-memory).** 1,000 concurrent transfers among five accounts. Checks that no customer balance is negative, the customers' total is unchanged, every cached balance equals its ledger sum, and the ledger nets to zero across all accounts.
- **PostgreSQL (manual, Docker Compose).** 500 concurrent `POST /transfers` with 50 workers on the real database: 464 committed, 36 rejected for insufficient funds, no 409 or 500. Afterwards, every account's cached balance matched its ledger sum, no transaction netted to a non-zero amount, and no customer balance was negative.

## Consequences

- Transfers can only be as fast as the hottest account allows. A high-traffic merchant account would be a bottleneck, and that case would need a different design (for example, a queue per account).
- The lock order is defined by the database's `uuid` ordering. The in-memory fake uses the .NET `Guid` order. Each side is internally consistent, which is what deadlock avoidance needs, but the two orders are not the same.
- Any new operation that locks more than one account must use the same ascending-id order.

## Open questions

- **No lock timeout.** A transfer waiting on a stuck transaction waits indefinitely. A `lock_timeout` (or `statement_timeout`) should be set so requests fail with a clear error instead of hanging.
- **Single-account operations are not locked.** Deposits and withdrawals rely on the version check, so under heavy contention they fail with 409 instead of waiting. If clients cannot handle 409, they should use `FOR UPDATE` too.
- **Idempotency.** As with ADR 0004, a retried transfer creates a second transfer. An idempotency key is still needed.
