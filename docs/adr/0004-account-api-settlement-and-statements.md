# ADR 0004: Account API, settlement counterpart, statement pagination, and error mapping

## Context

The first use cases and HTTP endpoints are: create account, read account, deposit, withdrawal, and statement.
Every money movement must produce balanced ledger entries (ADR 0002), so each deposit or withdrawal needs a
counterpart account. ADR 0002 left this open. The statement must paginate over a ledger that only grows.

## Decisions

### 1. A settlement account is the counterpart of deposits and withdrawals

- `AccountKind` has two values: `Customer` and `Settlement`.
- A customer account can never go negative. A settlement account may go negative, because it represents money held outside the wallet. Deposit: customer +X, settlement -X. Withdrawal: customer -X, settlement +X.
- The settlement account has a well-known id (`SettlementAccount.Id`, BRL only).
- `EnsureSettlementAccountUseCase` runs at API startup and creates the account if it is missing. It is idempotent. Startup is the only place that creates it, so requests never race to create it.
- Trade-off: the rule "balance never negative" now has an explicit exception, scoped by kind. Multi-currency would need one settlement account per currency.

### 2. Use cases over one unit of work per request

- Each use case takes the repositories and `IUnitOfWork` it needs. It stages changes and commits once.
- A deposit or withdrawal loads both accounts, creates the balanced transaction, applies each entry, stores the transaction, and commits. The whole movement is one commit, so it is atomic (ADR 0003).
- Insufficient funds and blocked accounts fail during `ApplyEntry`, before the commit. Nothing is written.

### 3. Statement pagination by keyset cursor over the ledger sequence

- The ledger sequence (the persistence key of each entry) is the cursor source. Pages are ordered most recent first.
- The query asks for `limit + 1` rows. The extra row only tells whether another page exists.
- `nextCursor` is opaque: the last returned sequence, encoded as URL-safe base64. It is `null` on the last page.
- `limit` defaults to 20 and must be between 1 and 100. Malformed cursors are rejected with 400.
- Keyset pagination stays correct while new entries are appended. Offset pagination would skip or repeat entries.

### 4. Status codes and ProblemDetails

| Situation | Status | Source |
|---|---|---|
| Created | 201 with `Location` | `POST /accounts`, deposits, withdrawals |
| Malformed body, invalid input (decimals, limit, cursor, empty owner) | 400 | `RequestValidationException`, `BadHttpRequestException` |
| Account not found | 404 | `AccountNotFoundException` |
| Concurrent modification | 409 | `ConcurrencyConflictException` (ADR 0003) |
| Business rule (insufficient funds, blocked account, non-positive amount) | 422 | `DomainException` |

- `ApiExceptionHandler` (`IExceptionHandler`) maps these exceptions to `application/problem+json`.
- `UseStatusCodePages` gives a ProblemDetails body to error responses the framework sends without one (malformed request bodies), so every error uses the same format.
- The `Location` of a movement points to the account. A transaction resource does not exist yet.

### 5. Validation is in the use cases, for now

- Input rules that are not business rules (cent precision, limit range, cursor format, empty owner) throw `RequestValidationException`. They are checked in the use case, so the same rule applies to any entry point.
- The API maps them to 400 with a field-keyed `errors` object.

### 6. Feature folders for endpoints and use cases

- Application: `Accounts/`, `Movements/`, `Statements/`, `Settlement/`.
- API: `Features/Accounts`, `Features/Movements`, `Features/Statements`. Each feature defines a route group and its request and response records.

### 7. Test strategy for the API

- Unit tests run each use case over the in-memory fakes, through a shared harness (`WalletHarness`).
- Integration tests run the real pipeline (routing, binding, validation, exception mapping) through `WebApplicationFactory<Program>`. The `WalletApiFactory` replaces the EF Core persistence with the in-memory fakes and runs in the `Testing` environment, which skips the development migration step. No test connects to PostgreSQL.
- PostgreSQL behavior (SQL, the ledger join for statements, migrations) was verified by running the stack with Docker Compose.

## Consequences

- Money movements are atomic and balanced by construction. The integration tests check ledger balance per transaction.
- Adding a currency requires a settlement account for it.
- The statement endpoint depends on the ledger sequence. Changing the persistence key would change cursors.

## Open questions

- **Idempotency.** Retried deposits or withdrawals create duplicate movements. An idempotency key on movements is needed before these endpoints are used by a client that retries (CLAUDE.md focus). Not implemented yet.
- **Validation library.** FluentValidation was requested for validation and is waiting for approval. Validation is currently manual in the use cases.
- **OpenAPI UI.** Scalar was requested for the documentation UI and is waiting for approval. The built-in OpenAPI document (`/openapi/v1.json`) is available in Development.
- **Settlement creation across instances.** Two API instances starting at the same time could both try to create the settlement account. The second insert would fail. Only one instance should run the startup step, or the insert should tolerate a duplicate.
- **Transaction resource.** There is no `GET /transactions/{id}` yet, so movement responses cannot be followed.
- **Conflict over HTTP.** A 409 is covered by unit tests of the fakes and by the exception mapping, but no integration test produces a real concurrent conflict through HTTP.
