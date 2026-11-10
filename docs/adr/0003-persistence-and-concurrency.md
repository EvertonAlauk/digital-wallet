# ADR 0003: Persistence ports, optimistic concurrency, and PostgreSQL mapping

## Context

Use cases must change accounts and append transactions atomically, and two operations must never silently
overwrite each other's balance changes. The application layer must not depend on EF Core or PostgreSQL, and
the tests must run without a database (CLAUDE.md).

## Decisions

### 1. Ports in Application; the unit of work is the atomic boundary

Application defines `IAccountRepository`, `ITransactionRepository`, `IUnitOfWork` and `IClock`.

- Repositories only stage changes. Nothing is persisted until `IUnitOfWork.CommitAsync`.
- `CommitAsync` persists everything changed in the operation, or nothing.
- `ConcurrencyConflictException` (Application) signals that an account changed after it was read.
  Use cases catch it and may retry after reloading.

### 2. Optimistic concurrency with `Account.Version`

- Every movement increments `Account.Version` (domain rule, ADR 0002).
- A status change (`Block`/`Unblock`) also increments `Version`. Without this, a stale balance change could
  overwrite a block with no conflict detected. A regression test covers this case.
- Commit is a compare-and-swap: an account changed by the unit of work is written only if its stored version
  equals the version read. The check covers every changed account before any write.

### 3. In-memory fakes emulate the database

`tests/DigitalWallet.Testing` contains `InMemoryDatabase` and `InMemoryUnitOfWork`:

- Each unit of work works on private copies, so uncommitted changes are invisible to others.
- Commit has two phases. Phase 1 checks every version and duplicate key. Phase 2 publishes. Nothing is written
  if phase 1 fails (all-or-nothing).
- A lock makes the database thread-safe. The fakes are validated by tests in `UnitTests/Fakes`, including
  atomicity, stale reads, identity and concurrent credits with retry.

### 4. EF Core + Npgsql mapping

- `Money` is mapped as a complex property: `numeric(18,2)` for the amount plus a currency column.
- `Version` is an `IsConcurrencyToken`. EF generates `UPDATE ... WHERE Version = <read version>`.
- `DbUpdateConcurrencyException` is translated to `ConcurrencyConflictException` in `UnitOfWork`.
  Use cases never see EF types.
- `LedgerEntry` has no domain identity, so it gets a persistence-only `long` surrogate key. The domain model is
  unchanged. `WalletDbContext` rejects any tracked update or delete of a ledger entry (append-only guard).
- Domain entities keep a private parameterless constructor for EF materialization. The Domain project has no
  EF dependency. `Rehydrate` factories restore persisted state without re-running creation rules.
- `Transaction.Entries` is a view over a private list. The list is mapped by field name and the view is ignored.

### 5. Migrations

- The first migration (`InitialCreate`) is generated with `dotnet ef`. A design-time factory allows generating
  migrations without starting the API or connecting to a database.
- In Development, the API applies pending migrations on startup. Other environments do not apply migrations
  automatically yet; a deployment step will be defined later.

## Consequences

- Concurrency logic is tested without a database. Mapping behavior (SQL, constraints, the concurrency token
  in UPDATE statements) is verified by running the app with Docker Compose.
- Retries on `ConcurrencyConflictException` are the caller's responsibility.
- The ledger's append-only rule is enforced by the domain (no mutating operation) and by the DbContext guard.
  A database trigger is not in place yet.

## Open questions

- **Transaction status changes are not persisted yet.** `MarkAsReversed` changes the in-memory status, but
  `ITransactionRepository` has no update path and the fake does not track transaction changes. This is needed
  when reversal is implemented.
- **Conflict details.** `ConcurrencyConflictException` identifies the account, not the operation.
- **Duplicate keys.** The fake reports a duplicate insert as `InvalidOperationException`. EF surfaces it as
  `DbUpdateException`, which is not translated yet.
